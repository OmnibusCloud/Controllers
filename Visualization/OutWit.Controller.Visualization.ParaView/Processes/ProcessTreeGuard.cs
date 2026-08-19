using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OutWit.Controller.Visualization.ParaView.Processes;

/// <summary>
/// Ties spawned pvpython child processes to the lifetime of THIS process, so a
/// crashed or force-killed worker cannot leave orphans pinning the node's CPU.
/// On Windows all children are assigned to a single Job Object with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: the job handle is deliberately never closed, the OS
/// closes it when this process dies (for ANY reason, including a hard kill), which kills every
/// process in the job. On POSIX no parent-death primitive is reachable from the spawning side
/// (PR_SET_PDEATHSIG must be set inside the child and is Linux-only), so this is a no-op there —
/// the cancellation path still kills the tree explicitly (the same POSIX orphan gap the Render
/// controller documents).
/// </summary>
internal static class ProcessTreeGuard
{
    #region Constants

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private const int JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS = 9;

    #endregion

    #region Fields

    private static readonly object LOCK = new();

    private static nint m_jobHandle;

    private static bool m_jobUnavailable;

    #endregion

    #region Functions

    /// <summary>
    /// Assigns a freshly started child process to the shared kill-on-close job object. Best
    /// effort: a failure is logged but never breaks the task (the explicit cancellation kill
    /// path is unaffected).
    /// </summary>
    public static void AttachToParentLifetime(System.Diagnostics.Process process, ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var job = EnsureJob(logger);
            if (job == 0)
                return;

            if (!AssignProcessToJobObject(job, process.Handle))
            {
                logger?.LogWarning(
                    "Failed to assign child process {Pid} to the kill-on-close job object (error {Error}); it may orphan if this process dies",
                    process.Id, Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex,
                "Child-process lifetime guard could not be attached; the child may orphan if this process dies");
        }
    }

    #endregion

    #region Tools

    private static nint EnsureJob(ILogger? logger)
    {
        lock (LOCK)
        {
            if (m_jobHandle != 0 || m_jobUnavailable)
                return m_jobHandle;

            m_jobHandle = CreateKillOnCloseJob();
            if (m_jobHandle == 0)
            {
                m_jobUnavailable = true;
                logger?.LogWarning(
                    "Kill-on-close job object could not be created (error {Error}); spawned runner processes may orphan if this process dies",
                    Marshal.GetLastWin32Error());
            }

            // The handle is deliberately never closed: the OS closing it at THIS process's death
            // (normal exit, crash, or hard kill) is exactly what kills the children.
            return m_jobHandle;
        }
    }

    /// <summary>
    /// Creates a job object configured to kill all member processes when its last handle closes.
    /// Internal so tests can verify the kill-on-close semantics against a disposable handle.
    /// </summary>
    internal static nint CreateKillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        var job = CreateJobObjectW(0, null);
        if (job == 0)
            return 0;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION_CLASS, buffer, (uint)size))
            {
                CloseHandle(job);
                return 0;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return job;
    }

    internal static bool AssignToJob(nint job, System.Diagnostics.Process process)
    {
        return OperatingSystem.IsWindows() && AssignProcessToJobObject(job, process.Handle);
    }

    internal static void CloseJob(nint job)
    {
        if (OperatingSystem.IsWindows() && job != 0)
            CloseHandle(job);
    }

    #endregion

    #region Win32

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint hJob, int jobObjectInformationClass,
        nint lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    #endregion
}
