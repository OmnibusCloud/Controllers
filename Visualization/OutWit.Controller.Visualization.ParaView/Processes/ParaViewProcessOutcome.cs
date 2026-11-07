namespace OutWit.Controller.Visualization.ParaView.Processes;

/// <summary>
/// The outcome of one pvpython run: exit code, wall-clock seconds, and the bounded tails of stdout and stderr.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="ElapsedSeconds">Wall-clock duration.</param>
/// <param name="StdoutTail">Bounded stdout tail.</param>
/// <param name="StderrTail">Bounded stderr tail.</param>
/// <param name="TimedOut">True when the wall-clock limit killed the process.</param>
public sealed record ParaViewProcessOutcome(int ExitCode, double ElapsedSeconds, string StdoutTail, string StderrTail, bool TimedOut);
