using System.Diagnostics;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Outcome of one ccx run: forwarded exit code, measured wall time and the
/// tail of the merged output stream (the error tail of a failed solve).
/// </summary>
public sealed class CcxRunOutcome
{
    #region Constructors

    /// <summary>
    /// Captures one finished run.
    /// </summary>
    /// <param name="exitCode">Solver process exit code.</param>
    /// <param name="elapsedSeconds">Measured wall-clock duration in seconds.</param>
    /// <param name="logTail">Last lines of merged stdout/stderr.</param>
    public CcxRunOutcome(int exitCode, double elapsedSeconds, string logTail)
    {
        ExitCode = exitCode;
        ElapsedSeconds = elapsedSeconds;
        LogTail = logTail;
    }

    #endregion

    #region Properties

    /// <summary>Solver process exit code; 0 = success.</summary>
    public int ExitCode { get; }

    /// <summary>Measured wall-clock duration in seconds.</summary>
    public double ElapsedSeconds { get; }

    /// <summary>Last lines of merged stdout/stderr.</summary>
    public string LogTail { get; }

    #endregion
}

/// <summary>
/// Spawns the real ccx with its host contract: bare jobname argument (no -i),
/// cwd = the job directory, OMP_NUM_THREADS set explicitly, kill propagated
/// to the whole process tree on cancellation, exit code forwarded verbatim.
/// </summary>
public static class CcxProcessRunner
{
    #region Constants

    private const int LOG_TAIL_LINES = 60;

    #endregion

    #region Functions

    /// <summary>
    /// Runs one solve to completion.
    /// </summary>
    /// <param name="solverPath">Full path of the ccx executable.</param>
    /// <param name="jobName">Bare job name; ccx reads &lt;jobName&gt;.inp in the job directory.</param>
    /// <param name="jobDirectory">Directory holding the deck; results land here.</param>
    /// <param name="threads">OMP thread count; 0 = all cores of this machine.</param>
    /// <param name="cancellationToken">Kills the whole solver process tree when signaled.</param>
    /// <returns>The run's outcome.</returns>
    public static async Task<CcxRunOutcome> RunAsync(
        string solverPath,
        string jobName,
        string jobDirectory,
        int threads,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(solverPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = jobDirectory
        };

        startInfo.ArgumentList.Add(jobName);
        startInfo.EnvironmentVariables["OMP_NUM_THREADS"] =
            (threads > 0 ? threads : Environment.ProcessorCount).ToString();

        var tail = new Queue<string>(LOG_TAIL_LINES);

        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, args) => AppendLine(tail, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(tail, args.Data);

        var stopwatch = Stopwatch.StartNew();

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var kill = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The process may already be gone; the kill is best-effort.
            }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        stopwatch.Stop();

        string logTail;
        lock (tail)
        {
            logTail = string.Join('\n', tail);
        }

        return new CcxRunOutcome(process.ExitCode, stopwatch.Elapsed.TotalSeconds, logTail);
    }

    private static void AppendLine(Queue<string> tail, string? line)
    {
        if (line == null)
            return;

        lock (tail)
        {
            if (tail.Count == LOG_TAIL_LINES)
                tail.Dequeue();

            tail.Enqueue(line);
        }
    }

    #endregion
}
