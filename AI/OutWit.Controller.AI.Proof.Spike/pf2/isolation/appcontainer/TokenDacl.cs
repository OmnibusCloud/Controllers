using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Proof.Spike.Sandbox;

/// <summary>
/// A process token's default DACL: the access that every object the process creates without a
/// security descriptor of its own receives (the pipe of <c>CreatePipe</c> among them). Read as SDDL
/// with the SIDs of the machine's accounts replaced by their role, and set to one that grants the
/// AppContainer's SID. Shared by the launcher and the escape probe (linked into both).
/// </summary>
internal static class TokenDacl
{
    #region Constants

    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const int TOKEN_DEFAULT_DACL = 6;
    private const uint SDDL_REVISION_1 = 1;
    private const uint SECURITY_DESCRIPTOR_REVISION = 1;
    private const uint DACL_SECURITY_INFORMATION = 4;
    private const int SECURITY_DESCRIPTOR_SIZE = 64;

    #endregion

    #region Functions

    /// <summary>
    /// The default DACL of a process's token as SDDL, the machine's own SIDs replaced by their role
    /// (user, logon, container, capability).
    /// </summary>
    /// <param name="process">A handle to the process (PROCESS_QUERY_INFORMATION or the current process).</param>
    /// <returns>The SDDL, or what failed.</returns>
    public static string Describe(IntPtr process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token))
            return $"token not opened, error {Marshal.GetLastWin32Error()}";
        try
        {
            GetTokenInformation(token, TOKEN_DEFAULT_DACL, IntPtr.Zero, 0, out var size);
            if (size <= 0)
                return $"default DACL size not read, error {Marshal.GetLastWin32Error()}";
            var buffer = Marshal.AllocHGlobal(size);
            var descriptor = Marshal.AllocHGlobal(SECURITY_DESCRIPTOR_SIZE);
            try
            {
                if (!GetTokenInformation(token, TOKEN_DEFAULT_DACL, buffer, size, out _))
                    return $"default DACL not read, error {Marshal.GetLastWin32Error()}";
                var dacl = Marshal.ReadIntPtr(buffer);
                if (dacl == IntPtr.Zero)
                    return "no default DACL";
                if (!InitializeSecurityDescriptor(descriptor, SECURITY_DESCRIPTOR_REVISION)
                    || !SetSecurityDescriptorDacl(descriptor, true, dacl, false)
                    || !ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, SDDL_REVISION_1, DACL_SECURITY_INFORMATION, out var text, IntPtr.Zero))
                    return $"default DACL not converted, error {Marshal.GetLastWin32Error()}";
                var sddl = Marshal.PtrToStringUni(text) ?? "";
                LocalFree(text);
                return Anonymize(sddl);
            }
            finally
            {
                Marshal.FreeHGlobal(descriptor);
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// Sets the default DACL of a (suspended) process's token to one granting SYSTEM, the
    /// Administrators and the AppContainer's SID all access.
    /// </summary>
    /// <param name="process">A handle to the process.</param>
    /// <param name="containerSid">The AppContainer's SID as a string.</param>
    /// <returns>"set", or what failed.</returns>
    public static string Grant(IntPtr process, string containerSid)
    {
        if (!OpenProcessToken(process, TOKEN_ADJUST_DEFAULT | TOKEN_QUERY, out var token))
            return $"token not opened, error {Marshal.GetLastWin32Error()}";
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{containerSid})",
                    SDDL_REVISION_1, out var descriptor, IntPtr.Zero))
                return $"descriptor not built, error {Marshal.GetLastWin32Error()}";
            try
            {
                if (!GetSecurityDescriptorDacl(descriptor, out _, out var dacl, out _))
                    return $"DACL not taken, error {Marshal.GetLastWin32Error()}";
                var info = Marshal.AllocHGlobal(IntPtr.Size);
                try
                {
                    Marshal.WriteIntPtr(info, dacl);
                    return SetTokenInformation(token, TOKEN_DEFAULT_DACL, info, IntPtr.Size)
                        ? "set"
                        : $"not set, error {Marshal.GetLastWin32Error()}";
                }
                finally
                {
                    Marshal.FreeHGlobal(info);
                }
            }
            finally
            {
                LocalFree(descriptor);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static string Anonymize(string sddl)
    {
        sddl = Regex.Replace(sddl, @"S-1-15-2-[0-9-]+", "container");
        sddl = Regex.Replace(sddl, @"S-1-15-3-[0-9-]+", "capability");
        sddl = Regex.Replace(sddl, @"S-1-5-21-[0-9-]+", "user");
        sddl = Regex.Replace(sddl, @"S-1-5-5-[0-9]+-[0-9]+", "logon");
        return sddl;
    }

    #endregion

    #region Native

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        int tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool InitializeSecurityDescriptor(IntPtr securityDescriptor, uint revision);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetSecurityDescriptorDacl(IntPtr securityDescriptor, bool daclPresent, IntPtr dacl, bool daclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr securityDescriptor, out bool daclPresent, out IntPtr dacl,
        out bool daclDefaulted);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW")]
    private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptor(IntPtr securityDescriptor, uint revision,
        uint securityInformation, out IntPtr text, IntPtr textLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW")]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision,
        out IntPtr securityDescriptor, IntPtr size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}
