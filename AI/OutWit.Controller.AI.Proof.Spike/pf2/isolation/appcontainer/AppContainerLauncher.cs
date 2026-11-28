using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static OutWit.Controller.AI.Proof.Spike.Sandbox.NativeMethods;

namespace OutWit.Controller.AI.Proof.Spike.Sandbox;

/// <summary>
/// Starts one command in an AppContainer (no capabilities: no network, no access to the user's
/// files; read and execute only on the folders granted, read and write only on the job's) inside a
/// Job Object (a memory cap for the whole tree, a process cap, the tree ended when the job closes
/// or the wall cap passes), and reports the job's own accounting.
/// </summary>
public static class AppContainerLauncher
{
    #region Constants

    private const string DISPLAY_NAME = "PF-2 kernel sandbox";

    private const uint WALL_CAP_EXIT = 0xC0DE0001;

    #endregion

    #region Profile

    /// <summary>
    /// Creates the AppContainer profile, or finds the one of that name.
    /// </summary>
    /// <param name="name">The profile's name.</param>
    /// <returns>The container's SID as a string.</returns>
    /// <exception cref="Win32Exception">The profile could not be made.</exception>
    public static string EnsureProfile(string name)
    {
        var hr = CreateAppContainerProfile(name, DISPLAY_NAME, DISPLAY_NAME, IntPtr.Zero, 0, out var sid);
        if (hr == HRESULT_ALREADY_EXISTS)
            hr = DeriveAppContainerSidFromAppContainerName(name, out sid);
        if (hr != 0)
            throw new Win32Exception(hr, $"AppContainer profile {name}: 0x{hr:X8}");
        try
        {
            return SidString(sid);
        }
        finally
        {
            FreeSid(sid);
        }
    }

    /// <summary>
    /// Deletes the AppContainer profile (its registry entries and its folder under the user's
    /// local application data).
    /// </summary>
    /// <param name="name">The profile's name.</param>
    /// <returns>The HRESULT.</returns>
    public static int DeleteProfile(string name)
    {
        return DeleteAppContainerProfile(name);
    }

    /// <summary>
    /// Grants the container a folder and everything in it.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="sid">The container's SID.</param>
    /// <param name="write">Modify (the job's folder) or read and execute (the toolchain).</param>
    public static void Grant(string folder, string sid, bool write)
    {
        Icacls(folder, "/grant", $"*{sid}:(OI)(CI)({(write ? "M" : "RX")})");
    }

    /// <summary>
    /// Removes every entry of the container from a folder and everything in it.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <param name="sid">The container's SID.</param>
    public static void Revoke(string folder, string sid)
    {
        Icacls(folder, "/remove", $"*{sid}");
    }

    private static void Icacls(string folder, string verb, string what)
    {
        var info = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { folder, verb, what, "/T", "/C", "/Q" })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"icacls {folder} {verb} {what}: exit {process.ExitCode}: {said.Trim()}");
    }

    private static string SidString(IntPtr sid)
    {
        if (!ConvertSidToStringSid(sid, out var text))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            return Marshal.PtrToStringUni(text)!;
        }
        finally
        {
            LocalFree(text);
        }
    }

    #endregion

    #region Run

    /// <summary>
    /// Runs the command of the options to its end.
    /// </summary>
    /// <param name="options">What the run may reach and its caps.</param>
    /// <returns>How it ended and what it cost.</returns>
    public static SandboxResult Run(SandboxOptions options)
    {
        IntPtr sid = IntPtr.Zero;
        string? sidText = null;
        double grantSeconds = 0;
        if (options.Container)
        {
            sidText = EnsureProfile(options.Name);
            var hr = DeriveAppContainerSidFromAppContainerName(options.Name, out sid);
            if (hr != 0)
                throw new Win32Exception(hr);
            var clock = Stopwatch.StartNew();
            Grant(options.JobDir, sidText, true);
            foreach (var dir in options.ReadDirs)
                Grant(dir, sidText, false);
            grantSeconds = clock.Elapsed.TotalSeconds;
        }
        var commandLine = BuildCommandLine(options.Command);
        var result = new SandboxResult
        {
            CommandLine = commandLine, Container = options.Container, Lpac = options.Lpac, ContainerSid = sidText, Console = options.Console,
            MemoryCap = (ulong)options.MemoryMb << 20, WallCap = options.WallSeconds, GrantSeconds = Math.Round(grantSeconds, 2),
        };
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var handles = new List<IntPtr>();
        var allocations = new List<IntPtr>();
        var attributes = IntPtr.Zero;
        var info = new PROCESS_INFORMATION();
        try
        {
            ConfigureJob(job, options);
            var stdin = OpenFile("NUL", false);
            var stdout = OpenFile(options.StdoutPath ?? Path.Combine(options.JobDir, "pf2-stdout.txt"), true);
            var stderr = OpenFile(options.StderrPath ?? Path.Combine(options.JobDir, "pf2-stderr.txt"), true);
            handles.AddRange([stdin, stdout, stderr]);
            var count = 1 + (options.Container ? 1 : 0) + (options.Lpac ? 1 : 0) + (options.NoChildren ? 1 : 0);
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, count, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, count, 0, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var handleList = Marshal.AllocHGlobal(IntPtr.Size * 3);
            allocations.Add(handleList);
            Marshal.WriteIntPtr(handleList, 0, stdin);
            Marshal.WriteIntPtr(handleList, IntPtr.Size, stdout);
            Marshal.WriteIntPtr(handleList, IntPtr.Size * 2, stderr);
            Update(attributes, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, handleList, IntPtr.Size * 3);
            if (options.Container)
            {
                var caps = new SECURITY_CAPABILITIES { AppContainerSid = sid, Capabilities = IntPtr.Zero, CapabilityCount = 0 };
                var capsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<SECURITY_CAPABILITIES>());
                allocations.Add(capsPtr);
                Marshal.StructureToPtr(caps, capsPtr, false);
                Update(attributes, PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES, capsPtr, Marshal.SizeOf<SECURITY_CAPABILITIES>());
            }
            if (options.Lpac)
                Update(attributes, PROC_THREAD_ATTRIBUTE_ALL_APPLICATION_PACKAGES_POLICY, IntValue(PROCESS_CREATION_ALL_APPLICATION_PACKAGES_OPT_OUT, allocations), 4);
            if (options.NoChildren)
                Update(attributes, PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY, IntValue(PROCESS_CREATION_CHILD_PROCESS_RESTRICTED, allocations), 4);
            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
            if (options.Console == "new")
            {
                startup.StartupInfo.dwFlags |= STARTF_USESHOWWINDOW;
                startup.StartupInfo.wShowWindow = SW_HIDE;
            }
            startup.StartupInfo.hStdInput = stdin;
            startup.StartupInfo.hStdOutput = stdout;
            startup.StartupInfo.hStdError = stderr;
            startup.lpAttributeList = attributes;
            var environment = Marshal.StringToHGlobalUni(BuildEnvironment(options));
            allocations.Add(environment);
            var console = options.Console switch
            {
                "hidden" => CREATE_NO_WINDOW,
                "new" => CREATE_NEW_CONSOLE,
                _ => DETACHED_PROCESS,
            };
            var flags = EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | console | BELOW_NORMAL_PRIORITY_CLASS;
            if (!CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, true, flags, environment,
                    options.JobDir, ref startup, out info))
            {
                result.Ending = "could_not_start";
                result.StartError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return result;
            }
            // The default DACL of the container's token: what every object it creates without a
            // descriptor of its own (CreatePipe's pipe) grants; set to grant the container itself.
            result.DefaultDacl = TokenDacl.Describe(info.hProcess);
            if (options.DefaultDacl && sidText is not null)
            {
                result.DefaultDaclSet = TokenDacl.Grant(info.hProcess, sidText);
                result.DefaultDacl = TokenDacl.Describe(info.hProcess);
            }
            if (!AssignProcessToJobObject(job, info.hProcess))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var clock = Stopwatch.StartNew();
            ResumeThread(info.hThread);
            var waited = WaitForSingleObject(info.hProcess, (uint)Math.Min(options.WallSeconds * 1000, uint.MaxValue - 1));
            var wallCap = waited == WAIT_TIMEOUT;
            if (wallCap)
            {
                TerminateJobObject(job, WALL_CAP_EXIT);
                WaitForSingleObject(info.hProcess, 30_000);
            }
            result.WallSeconds = Math.Round(clock.Elapsed.TotalSeconds, 2);
            GetExitCodeProcess(info.hProcess, out var code);
            result.ExitCode = code;
            Account(job, result);
            if (result.LeftOver > 0)
                TerminateJobObject(job, 0);
            result.Ending = wallCap ? "wall_cap"
                : code == 0 ? "accepted"
                : result.MemoryCap > 0 && result.PeakJobMemory >= result.MemoryCap - (64UL << 20) ? "memory_cap"
                : code >= 0xC0000000 ? "crashed"
                : "rejected";
        }
        finally
        {
            if (info.hThread != IntPtr.Zero)
                CloseHandle(info.hThread);
            if (info.hProcess != IntPtr.Zero)
                CloseHandle(info.hProcess);
            foreach (var handle in handles)
                CloseHandle(handle);
            if (attributes != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            foreach (var allocation in allocations)
                Marshal.FreeHGlobal(allocation);
            CloseHandle(job);
            if (sid != IntPtr.Zero)
                FreeSid(sid);
        }
        result.Stdout = Tail(options.StdoutPath ?? Path.Combine(options.JobDir, "pf2-stdout.txt"));
        result.Stderr = Tail(options.StderrPath ?? Path.Combine(options.JobDir, "pf2-stderr.txt"));
        return result;
    }

    private static void ConfigureJob(IntPtr job, SandboxOptions options)
    {
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION
                                                  | JOB_OBJECT_LIMIT_PRIORITY_CLASS;
        limits.BasicLimitInformation.PriorityClass = BELOW_NORMAL_PRIORITY_CLASS;
        if (options.MemoryMb > 0)
        {
            limits.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_JOB_MEMORY;
            limits.JobMemoryLimit = (UIntPtr)((ulong)options.MemoryMb << 20);
        }
        if (options.Processes > 0)
        {
            limits.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            limits.BasicLimitInformation.ActiveProcessLimit = (uint)options.Processes;
        }
        SetJob(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION, limits);
        SetJob(job, JOB_OBJECT_BASIC_UI_RESTRICTIONS, new JOBOBJECT_BASIC_UI_RESTRICTIONS { UIRestrictionsClass = JOB_OBJECT_UILIMIT_ALL });
    }

    private static void SetJob<T>(IntPtr job, int infoClass, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            if (!SetInformationJobObject(job, infoClass, buffer, size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static T QueryJob<T>(IntPtr job, int infoClass) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(job, infoClass, buffer, size, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return Marshal.PtrToStructure<T>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Account(IntPtr job, SandboxResult result)
    {
        var limits = QueryJob<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION);
        result.PeakJobMemory = (ulong)limits.PeakJobMemoryUsed;
        result.PeakProcessMemory = (ulong)limits.PeakProcessMemoryUsed;
        var accounting = QueryJob<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(job, JOB_OBJECT_BASIC_ACCOUNTING_INFORMATION);
        result.UserSeconds = Math.Round(accounting.TotalUserTime / 1e7, 2);
        result.KernelSeconds = Math.Round(accounting.TotalKernelTime / 1e7, 2);
        result.TotalProcesses = accounting.TotalProcesses;
        result.LeftOver = accounting.ActiveProcesses;
    }

    private static void Update(IntPtr attributes, IntPtr attribute, IntPtr value, int size)
    {
        if (!UpdateProcThreadAttribute(attributes, 0, attribute, value, size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static IntPtr IntValue(int value, List<IntPtr> allocations)
    {
        var pointer = Marshal.AllocHGlobal(4);
        allocations.Add(pointer);
        Marshal.WriteInt32(pointer, value);
        return pointer;
    }

    private static IntPtr OpenFile(string path, bool write)
    {
        var security = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = 1 };
        var handle = CreateFile(path, write ? GENERIC_WRITE : GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, ref security,
            write ? CREATE_ALWAYS : OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), path);
        return handle;
    }

    private static string BuildEnvironment(SandboxOptions options)
    {
        var system = System.Environment.GetFolderPath(System.Environment.SpecialFolder.System);
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows),
            ["SystemDrive"] = Path.GetPathRoot(system)!.TrimEnd('\\'),
            ["TEMP"] = options.JobDir,
            ["TMP"] = options.JobDir,
            ["PATH"] = string.Join(';', options.ReadDirs.Select(d => Path.Combine(d, "bin")).Append(system)),
        };
        // CreateProcess points an AppContainer's TEMP, TMP and LOCALAPPDATA at the container's own
        // folder, and refuses (error 203) when the block lacks the profile's variables.
        foreach (var name in new[] { "USERPROFILE", "LOCALAPPDATA", "APPDATA" })
        {
            var value = System.Environment.GetEnvironmentVariable(name);
            if (value is not null)
                variables[name] = value;
        }
        foreach (var pair in options.Environment)
        {
            var at = pair.IndexOf('=');
            variables[pair[..at]] = pair[(at + 1)..];
        }
        var block = new StringBuilder();
        foreach (var (key, value) in variables)
            block.Append(key).Append('=').Append(value).Append('\0');
        block.Append('\0');
        return block.ToString();
    }

    private static string BuildCommandLine(IEnumerable<string> args)
    {
        var line = new StringBuilder();
        foreach (var arg in args)
        {
            if (line.Length > 0)
                line.Append(' ');
            if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            {
                line.Append(arg);
                continue;
            }
            line.Append('"');
            var slashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                line.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                slashes = 0;
                line.Append(c);
            }
            line.Append('\\', slashes * 2);
            line.Append('"');
        }
        return line.ToString();
    }

    private static string Tail(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Length > 4000 ? text[^4000..] : text;
        }
        catch (IOException)
        {
            return "";
        }
    }

    #endregion
}
