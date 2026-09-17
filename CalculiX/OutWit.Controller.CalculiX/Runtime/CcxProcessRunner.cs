using System.Diagnostics;

namespace OutWit.Controller.CalculiX.Runtime;

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
        var startInfo = CreateStartInfo(solverPath, jobName, jobDirectory, threads);

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

    /// <summary>
    /// The start parameters of one solve.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessStartInfo.CreateNoWindow"/> matters on Windows: the worker client is a
    /// windowed application without a console, so without it every ccx.exe (a console program)
    /// gets a console of its own, and creating it cost about 0.45 s per start on a Windows 11
    /// workstation - the reference cube took 1.13-1.23 s from the client against 0.69-0.76 s with
    /// the flag, and every variant of a sweep paid it. Output is redirected either way.
    /// </remarks>
    /// <param name="solverPath">Full path of the ccx executable.</param>
    /// <param name="jobName">Bare job name; ccx reads &lt;jobName&gt;.inp in the job directory.</param>
    /// <param name="jobDirectory">Directory holding the deck; results land here.</param>
    /// <param name="threads">OMP thread count; 0 = all cores of this machine.</param>
    /// <returns>The start info <see cref="RunAsync"/> launches.</returns>
    public static ProcessStartInfo CreateStartInfo(string solverPath, string jobName, string jobDirectory, int threads)
    {
        var startInfo = new ProcessStartInfo(solverPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = jobDirectory
        };

        startInfo.ArgumentList.Add(jobName);
        startInfo.EnvironmentVariables["OMP_NUM_THREADS"] =
            (threads > 0 ? threads : Environment.ProcessorCount).ToString();

        return startInfo;
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
