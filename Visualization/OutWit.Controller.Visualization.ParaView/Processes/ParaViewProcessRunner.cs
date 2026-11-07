using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Processes;

/// <summary>
/// Spawns pvpython with its host contract: the binary invoked directly with an argument array
/// (never a shell string), an allowlisted environment, cwd = the task's package root, bounded
/// stdout/stderr retention, the whole process tree killed on cancellation or on the wall-clock
/// limit, and the exit code forwarded verbatim. Children are tied to this process's lifetime on
/// Windows through the kill-on-close job object.
/// </summary>
public static class ParaViewProcessRunner
{
    #region Constants

    private static readonly TimeSpan DRAIN_TIMEOUT = TimeSpan.FromSeconds(10);

    #endregion

    #region Functions

    /// <summary>
    /// Runs pvpython to completion.
    /// </summary>
    /// <param name="pvpythonPath">Full path of the pvpython executable.</param>
    /// <param name="arguments">Argument array (runner script and its arguments).</param>
    /// <param name="workingDirectory">Working directory (the task's package root).</param>
    /// <param name="environment">The allowlisted environment; replaces the inherited one entirely.</param>
    /// <param name="wallClockLimit">Maximum run time before the tree is killed.</param>
    /// <param name="logger">Diagnostics sink.</param>
    /// <param name="cancellationToken">Kills the whole process tree when signaled.</param>
    /// <returns>The run's outcome.</returns>
    /// <exception cref="OperationCanceledException">The token was signaled.</exception>
    public static async Task<ParaViewProcessOutcome> RunAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan wallClockLimit,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(pvpythonPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment.Clear();
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;

        var stdout = new ParaViewProcessOutputTail(ParaViewInputLimits.MAX_PROCESS_OUTPUT_CHARS);
        var stderr = new ParaViewProcessOutputTail(ParaViewInputLimits.MAX_PROCESS_OUTPUT_CHARS);

        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, args) => stdout.Append(args.Data);
        process.ErrorDataReceived += (_, args) => stderr.Append(args.Data);

        var stopwatch = Stopwatch.StartNew();

        process.Start();
        ProcessTreeGuard.AttachToParentLifetime(process, logger);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(wallClockLimit);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(watchdog.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            timedOut = !cancellationToken.IsCancellationRequested;

            if (!timedOut)
                throw;
        }

        // Flush the async readers: WaitForExit(timeout) also waits for the redirected streams to
        // drain, which WaitForExitAsync does not guarantee. Bounded: an orphan that inherited the
        // pipes must not hold the task past the wall-clock limit.
        try
        {
            process.WaitForExit((int)DRAIN_TIMEOUT.TotalMilliseconds);
        }
        catch
        {
            // The process is gone either way; the tails hold what was read.
        }

        stopwatch.Stop();

        return new ParaViewProcessOutcome(
            timedOut ? -1 : process.ExitCode,
            stopwatch.Elapsed.TotalSeconds,
            stdout.Text,
            stderr.Text,
            timedOut);
    }

    #endregion

    #region Tools

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may already be gone; the kill is best-effort.
        }
    }

    #endregion
}
