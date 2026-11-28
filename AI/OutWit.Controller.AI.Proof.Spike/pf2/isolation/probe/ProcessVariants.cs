using System.Runtime.InteropServices;

namespace OutWit.Controller.AI.Proof.Spike.Probe;

/// <summary>
/// Windows: starts <c>cmd.exe /c exit 0</c> the way Lean's runtime does (<c>CreateProcessA</c>, no
/// application name, a quoted command line, handles inherited, the standard handles given as
/// inheritable duplicates, no environment block, no flags; src/runtime/process.cpp) and in variants
/// that change one thing each, to tell which part a sandbox refuses.
/// </summary>
internal static class ProcessVariants
{
    #region Constants

    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private const uint DUPLICATE_SAME_ACCESS = 2;

    /// <summary>The variants, in the order they run.</summary>
    public static readonly string[] NAMES =
    [
        "as Lean: CreateProcessA, quoted name, inherit, std handles, no environment",
        "CreateProcessW instead of A",
        "an environment block (ANSI) instead of none",
        "CreateProcessW with a Unicode environment block (as .NET)",
        "no handles inherited",
        "no standard handles given",
        "the full path of cmd.exe in the command line",
    ];

    #endregion

    #region Functions

    /// <summary>
    /// Runs one variant.
    /// </summary>
    /// <param name="index">The variant's index in <see cref="NAMES"/>.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="UnauthorizedAccessException">The start failed (the Win32 error in the message).</exception>
    public static string Run(int index)
    {
        var wide = index is 1 or 3;
        var environment = index switch
        {
            2 => GetEnvironmentStringsA(),
            3 => GetEnvironmentStringsW(),
            _ => IntPtr.Zero,
        };
        var command = index == 6
            ? $"\"{Path.Combine(System.Environment.SystemDirectory, "cmd.exe")}\" \"/c\" \"exit\" \"0\""
            : "\"cmd.exe\" \"/c\" \"exit\" \"0\"";
        var commandLine = wide ? Marshal.StringToHGlobalUni(command) : Marshal.StringToHGlobalAnsi(command);
        var duplicates = new List<IntPtr>();
        var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        if (index != 5)
        {
            startup.dwFlags = STARTF_USESTDHANDLES;
            startup.hStdInput = Duplicate(STD_INPUT_HANDLE, duplicates);
            startup.hStdOutput = Duplicate(STD_OUTPUT_HANDLE, duplicates);
            startup.hStdError = Duplicate(STD_ERROR_HANDLE, duplicates);
        }
        try
        {
            var inherit = index != 4;
            var flags = index == 3 ? CREATE_UNICODE_ENVIRONMENT : 0u;
            PROCESS_INFORMATION info;
            var created = wide
                ? CreateProcessW(IntPtr.Zero, commandLine, IntPtr.Zero, IntPtr.Zero, inherit, flags, environment, IntPtr.Zero, ref startup, out info)
                : CreateProcessA(IntPtr.Zero, commandLine, IntPtr.Zero, IntPtr.Zero, inherit, flags, environment, IntPtr.Zero, ref startup, out info);
            if (!created)
                throw new UnauthorizedAccessException($"CreateProcess failed, error {Marshal.GetLastPInvokeError()}");
            WaitForSingleObject(info.hProcess, 10000);
            GetExitCodeProcess(info.hProcess, out var code);
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            return $"started, exit {code}";
        }
        finally
        {
            foreach (var handle in duplicates)
                CloseHandle(handle);
            Marshal.FreeHGlobal(commandLine);
            if (index == 2)
                FreeEnvironmentStringsA(environment);
            if (index == 3)
                FreeEnvironmentStringsW(environment);
        }
    }

    private static IntPtr Duplicate(int which, List<IntPtr> duplicates)
    {
        var handle = GetStdHandle(which);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return handle;
        if (!DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), out var copy, 0, true, DUPLICATE_SAME_ACCESS))
            return handle;
        duplicates.Add(copy);
        return copy;
    }

    #endregion

    #region Native

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreateProcessA(IntPtr applicationName, IntPtr commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, IntPtr currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreateProcessW(IntPtr applicationName, IntPtr commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags, IntPtr environment, IntPtr currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetEnvironmentStringsA();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetEnvironmentStringsW();

    [DllImport("kernel32.dll")]
    private static extern bool FreeEnvironmentStringsA(IntPtr block);

    [DllImport("kernel32.dll")]
    private static extern bool FreeEnvironmentStringsW(IntPtr block);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr target,
        uint desiredAccess, bool inheritHandle, uint options);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    #endregion
}
