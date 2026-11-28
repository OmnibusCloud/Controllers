using System.Runtime.InteropServices;

namespace OutWit.Controller.AI.Proof.Spike.Probe;

/// <summary>
/// Windows: opens the null device the way Lean's runtime opens it for a child's standard input or
/// output (<c>CreateFile("NUL", ..., 0, inheritable, OPEN_EXISTING, ...)</c>, src/runtime/process.cpp),
/// in variants, and reads the device's security descriptor.
/// </summary>
internal static class NullDevice
{
    #region Constants

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint READ_CONTROL = 0x00020000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int SE_FILE_OBJECT = 1;
    private const uint DACL_SECURITY_INFORMATION = 4;
    private const uint LABEL_SECURITY_INFORMATION = 0x10;

    /// <summary>The variants: name, path, access, sharing, ANSI.</summary>
    public static readonly (string Name, string Path, uint Access, uint Share, bool Ansi)[] VARIANTS =
    [
        ("open NUL for reading as Lean does (CreateFileA, no sharing, inheritable)", "NUL", GENERIC_READ, 0, true),
        ("open NUL for writing as Lean does (CreateFileA, no sharing, inheritable)", "NUL", GENERIC_WRITE, 0, true),
        ("open \\\\.\\NUL for reading, shared (CreateFileW)", @"\\.\NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, false),
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Opens the null device as the variant says and closes it.
    /// </summary>
    /// <param name="index">The variant.</param>
    /// <returns>"opened".</returns>
    /// <exception cref="UnauthorizedAccessException">The open failed (the Win32 error in the message).</exception>
    public static string Open(int index)
    {
        var (_, path, access, share, ansi) = VARIANTS[index];
        var attributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
        var handle = ansi
            ? CreateFileA(path, access, share, ref attributes, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero)
            : CreateFileW(path, access, share, ref attributes, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            throw new UnauthorizedAccessException($"CreateFile failed, error {Marshal.GetLastPInvokeError()}");
        CloseHandle(handle);
        return "opened";
    }

    /// <summary>
    /// The null device's DACL and mandatory label as SDDL.
    /// </summary>
    /// <returns>The SDDL, or what failed.</returns>
    public static string Descriptor()
    {
        var attributes = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>() };
        var handle = CreateFileW(@"\\.\NUL", READ_CONTROL, FILE_SHARE_READ | FILE_SHARE_WRITE, ref attributes, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            return $"not opened for READ_CONTROL, error {Marshal.GetLastPInvokeError()}";
        try
        {
            var info = DACL_SECURITY_INFORMATION | LABEL_SECURITY_INFORMATION;
            var error = GetSecurityInfo(handle, SE_FILE_OBJECT, info, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var descriptor);
            if (error != 0)
                return $"security not read, error {error}";
            try
            {
                if (!ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, 1, info, out var text, IntPtr.Zero))
                    return $"not converted, error {Marshal.GetLastPInvokeError()}";
                var sddl = Marshal.PtrToStringUni(text) ?? "";
                LocalFree(text);
                return sddl;
            }
            finally
            {
                LocalFree(descriptor);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    #endregion

    #region Native

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "CreateFileA")]
    private static extern IntPtr CreateFileA(string fileName, uint access, uint share, ref SECURITY_ATTRIBUTES attributes,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern IntPtr CreateFileW(string fileName, uint access, uint share, ref SECURITY_ATTRIBUTES attributes,
        uint creation, uint flags, IntPtr template);

    [DllImport("advapi32.dll")]
    private static extern uint GetSecurityInfo(IntPtr handle, int objectType, uint securityInfo, IntPtr owner, IntPtr group,
        IntPtr dacl, IntPtr sacl, out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW")]
    private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptor(IntPtr descriptor, uint revision, uint info,
        out IntPtr text, IntPtr length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}
