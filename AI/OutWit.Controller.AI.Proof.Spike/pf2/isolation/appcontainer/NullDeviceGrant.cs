using System.Runtime.InteropServices;

namespace OutWit.Controller.AI.Proof.Spike.Sandbox;

/// <summary>
/// The null device for one AppContainer. Lean's runtime opens <c>NUL</c> for a child's standard input
/// whenever it starts one without input (<c>IO.Process.output</c>, so every child of
/// <c>lake comparator</c>), and on Windows Server 2022 and 2025 the null device's DACL has no entry for
/// application packages: inside an AppContainer that open is refused (NtCreateFile 0xC0000022) and
/// the child is never started. This adds one entry, read and write for the container's own SID, to
/// the device's DACL for the run, and puts the old DACL back after it. The null device holds no data:
/// writes are discarded, reads end at once. Needs an administrator (the device grants WRITE_DAC to
/// SYSTEM and the Administrators only).
/// </summary>
internal static class NullDeviceGrant
{
    #region Constants

    private const string NULL_DEVICE = @"\\.\NUL";
    private const uint READ_CONTROL = 0x00020000;
    private const uint WRITE_DAC = 0x00040000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const int SE_KERNEL_OBJECT = 6;
    private const uint DACL_SECURITY_INFORMATION = 4;
    private const uint SDDL_REVISION_1 = 1;
    private const string READ_WRITE = "0x1201bf";

    #endregion

    #region Functions

    /// <summary>
    /// Adds the container's entry to the null device's DACL.
    /// </summary>
    /// <param name="containerSid">The container's SID as a string.</param>
    /// <returns>The DACL before (SDDL), to restore; null when it could not be changed.</returns>
    /// <param name="said">What happened.</param>
    public static string? Grant(string containerSid, out string said)
    {
        var handle = Open();
        if (handle == new IntPtr(-1))
        {
            said = $"not opened for WRITE_DAC, error {Marshal.GetLastPInvokeError()}";
            return null;
        }
        try
        {
            var before = ReadDacl(handle, out said);
            if (before is null)
                return null;
            var after = before + $"(A;;{READ_WRITE};;;{containerSid})";
            said = WriteDacl(handle, after) ?? "granted";
            return said == "granted" ? before : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Puts the DACL the device had before back.
    /// </summary>
    /// <param name="before">The DACL before (SDDL).</param>
    /// <returns>What happened.</returns>
    public static string Restore(string before)
    {
        var handle = Open();
        if (handle == new IntPtr(-1))
            return $"not opened for WRITE_DAC, error {Marshal.GetLastPInvokeError()}";
        try
        {
            return WriteDacl(handle, before) ?? "restored";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static IntPtr Open()
    {
        return CreateFileW(NULL_DEVICE, READ_CONTROL | WRITE_DAC, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    private static string? ReadDacl(IntPtr handle, out string said)
    {
        var error = GetSecurityInfo(handle, SE_KERNEL_OBJECT, DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
        if (error != 0)
        {
            said = $"DACL not read, error {error}";
            return null;
        }
        try
        {
            if (!ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, SDDL_REVISION_1, DACL_SECURITY_INFORMATION, out var text, IntPtr.Zero))
            {
                said = $"DACL not converted, error {Marshal.GetLastPInvokeError()}";
                return null;
            }
            var sddl = Marshal.PtrToStringUni(text) ?? "";
            LocalFree(text);
            said = "read";
            return sddl;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static string? WriteDacl(IntPtr handle, string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, SDDL_REVISION_1, out var descriptor, IntPtr.Zero))
            return $"descriptor not built, error {Marshal.GetLastPInvokeError()}";
        try
        {
            if (!GetSecurityDescriptorDacl(descriptor, out _, out var dacl, out _))
                return $"DACL not taken, error {Marshal.GetLastPInvokeError()}";
            var error = SetSecurityInfo(handle, SE_KERNEL_OBJECT, DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
            return error == 0 ? null : $"DACL not set, error {error}";
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    #endregion

    #region Native

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateFileW(string fileName, uint access, uint share, IntPtr attributes, uint creation, uint flags, IntPtr template);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

    [DllImport("advapi32.dll")]
    private static extern uint SetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr securityDescriptor, out bool daclPresent, out IntPtr dacl, out bool daclDefaulted);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW")]
    private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptor(IntPtr descriptor, uint revision, uint info,
        out IntPtr text, IntPtr length);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr descriptor, IntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}
