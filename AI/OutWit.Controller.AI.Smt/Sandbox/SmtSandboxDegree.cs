using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// How many sessions a package runs at once on a node: the logical cores, a cap of
/// <see cref="MAX_DEGREE"/> (a worker is often a desktop, and a 32-core box should not vanish
/// under a package), and what the memory allows. One session may take, outside the process's
/// shared part (the engine, the compiled module), the host's cap of the solver's linear memory
/// twice over: the heap of the solver's exception objects is capped apart from the linear memory
/// and may take as much again. Beside that it takes a margin of host memory the caps do not count
/// (<see cref="SESSION_MARGIN_BYTES"/>). The memory counted is a share of what the machine has
/// available when the package starts, as the runtime sees it (the total less the load of every
/// process), so what the process already holds, the compiled module included, and everything
/// else that runs on the machine are taken.
/// <para>
/// A machine where not one session fits does not run the package: it is refused with
/// <see cref="SmtNodeFaults.MEMORY_INSUFFICIENT"/> and goes elsewhere: the repair round sends its
/// problems out again, to two machines. The planner sends a package only to a machine of at least
/// <see cref="MIN_RAM_MB"/>, so a refusal for memory is a machine whose memory was taken by other
/// work, or one that has not reported what it has. A machine that cannot say what it has available
/// runs as many sessions as its cores and the cap allow.
/// </para>
/// </summary>
internal static class SmtSandboxDegree
{
    #region Constants

    /// <summary>
    /// The least memory a machine must have for the planner to send it a package, in MiB of the
    /// total the node reports: <c>Smt.SolvePackage</c> declares it as its
    /// <c>RequiresResources.MinRamMb</c>, and the capabilities document states it. Why 8 GB: one
    /// session at the default options takes 2,752 MiB (<see cref="SessionBytes(SmtOptionsData)"/>),
    /// and the sessions may claim <see cref="RAM_HEADROOM"/> of the memory the machine has
    /// available, so one session needs 3,670 MiB available. An 8 GB machine has that with the
    /// node's client and its system running; a 6 GB machine has about 2 GB available then, and
    /// refuses every package with <see cref="SmtNodeFaults.MEMORY_INSUFFICIENT"/>.
    /// The planner admits a node that has reported no capabilities (it checks nothing then); such
    /// a node still refuses a package it has no memory for.
    /// </summary>
    public const long MIN_RAM_MB = 8192;

    /// <summary>The most sessions a package runs at once on a node.</summary>
    public const int MAX_DEGREE = 16;

    /// <summary>Share of the available memory the sessions may claim together.</summary>
    public const double RAM_HEADROOM = 0.75;

    /// <summary>
    /// The host memory one session takes beside its two caps: its thread's stack, the store and
    /// the instance, the output the host keeps (up to the session's output cap), the answers
    /// decoded as text, the problem as the sanitizer took it, and the row being written. Measured
    /// by <c>SmtSessionMemoryTests</c> (the live managed heap a session, four sessions at once, on
    /// Windows, Linux and macOS): under 2 MB for small problems, 58 to 83 MB for a problem of
    /// 3.2 MB with a model of 2 MB, and 99 to 125 MB for that problem enumerated up to the output
    /// cap (15.6 MB of models). The problem's part grows with its text (4 MiB at most) and the
    /// guest's stack takes up to 8 MiB more, so the worst session comes near 160 MB; the margin
    /// rounds that up.
    /// </summary>
    public const long SESSION_MARGIN_BYTES = 192L << 20;

    /// <summary>The degree was decided by the cores.</summary>
    public const string BY_CORES = "cores";

    /// <summary>The degree was decided by the cap of <see cref="MAX_DEGREE"/>.</summary>
    public const string BY_CAP = "cap";

    /// <summary>The degree was decided by the memory.</summary>
    public const string BY_MEMORY = "memory";

    /// <summary>Not one session fits in the memory: the degree is 0.</summary>
    public const string NO_MEMORY = "no_memory";

    #endregion

    #region Functions

    /// <summary>
    /// The memory one session may take for a cap of the solver's linear memory.
    /// </summary>
    /// <param name="hostMemoryBytes">The host's cap of the linear memory, bytes.</param>
    /// <returns>Twice the cap and the margin, bytes.</returns>
    public static long SessionBytes(long hostMemoryBytes)
    {
        return 2 * Math.Max(0, hostMemoryBytes) + SESSION_MARGIN_BYTES;
    }

    /// <summary>
    /// The memory one session may take under a run's options.
    /// </summary>
    /// <param name="options">The options; a number that is 0 is the default.</param>
    /// <returns>Bytes.</returns>
    public static long SessionBytes(SmtOptionsData options)
    {
        return SessionBytes(SmtProfile.Envelope(options).HostMemoryBytes);
    }

    /// <summary>
    /// The degree for a machine: <c>min(cores, MAX_DEGREE, usable / session)</c>, where usable is
    /// <see cref="RAM_HEADROOM"/> of the available memory; 0 when not one session fits.
    /// </summary>
    /// <param name="logicalCores">Logical processors.</param>
    /// <param name="availableBytes">The memory the machine has available; 0 or less when it cannot say (the cores decide).</param>
    /// <param name="sessionBytes">The memory one session may take (<see cref="SessionBytes(long)"/>).</param>
    /// <returns>The degree and what decided it.</returns>
    public static SmtDegree Compute(int logicalCores, long availableBytes, long sessionBytes)
    {
        var cores = Math.Max(1, logicalCores);
        var degree = Math.Min(cores, MAX_DEGREE);
        var reason = cores > MAX_DEGREE ? BY_CAP : BY_CORES;

        if (availableBytes <= 0 || sessionBytes <= 0)
            return new SmtDegree(degree, reason, cores, Math.Max(0, availableBytes), 0, sessionBytes);

        var usable = (long)(availableBytes * RAM_HEADROOM);
        var byMemory = usable / sessionBytes;
        if (byMemory < 1)
            return new SmtDegree(0, NO_MEMORY, cores, availableBytes, usable, sessionBytes);

        return byMemory < degree
            ? new SmtDegree((int)byMemory, BY_MEMORY, cores, availableBytes, usable, sessionBytes)
            : new SmtDegree(degree, reason, cores, availableBytes, usable, sessionBytes);
    }

    /// <summary>
    /// <see cref="Compute"/> for this machine now.
    /// </summary>
    /// <param name="sessionBytes">The memory one session may take.</param>
    /// <returns>The degree and what decided it.</returns>
    public static SmtDegree ForCurrentMachine(long sessionBytes)
    {
        return Compute(Environment.ProcessorCount, AvailableBytes(), sessionBytes);
    }

    /// <summary>
    /// The memory this machine has available as the runtime last saw it: the total less the load
    /// of every process; the whole of it when the load is not known; 0 when nothing is. The
    /// runtime's view is the same on the three platforms, and as fresh as its last collection.
    /// </summary>
    /// <returns>Bytes.</returns>
    public static long AvailableBytes()
    {
        var info = GC.GetGCMemoryInfo();
        return AvailableBytes(info.TotalAvailableMemoryBytes, info.MemoryLoadBytes);
    }

    /// <summary>The available memory for a total and a load.</summary>
    /// <param name="totalBytes">The memory the process may use in all; 0 or less when unknown.</param>
    /// <param name="loadBytes">The memory in use on the machine; 0 or less when unknown.</param>
    /// <returns>Bytes: 0 when the total is unknown, at least 1 otherwise.</returns>
    public static long AvailableBytes(long totalBytes, long loadBytes)
    {
        if (totalBytes <= 0)
            return 0;

        return loadBytes <= 0 ? totalBytes : Math.Max(1, totalBytes - loadBytes);
    }

    #endregion
}
