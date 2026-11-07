using System.Diagnostics;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Spawns one OpenFOAM process with the controller's contract: an explicit
/// environment and nothing inherited, cwd = the case directory, the merged
/// output written to the step's log file (OpenFOAM's own <c>log.&lt;utility&gt;</c>
/// convention, which the post step and the readers rely on), kill propagated
/// to the whole process tree on cancellation, exit code forwarded verbatim,
/// priority below normal.
/// </summary>
public static class FoamProcessRunner
{
    #region Constants

    private const int LOG_TAIL_LINES = 60;

    /// <summary>
    /// The priority a step runs at. A worker is often somebody's desktop: a
    /// parallel solve takes every core it is given, and at normal priority the
    /// person at the keyboard waits behind it. Below normal, the interactive
    /// programs get the CPU first and the solve takes what is left - all of it
    /// on an idle machine. Best-effort: a platform that refuses keeps normal.
    /// </summary>
    public const ProcessPriorityClass RUN_PRIORITY = ProcessPriorityClass.BelowNormal;

    #endregion

    #region Functions

    /// <summary>
    /// Runs one process to completion.
    /// </summary>
    /// <param name="fileName">Full path of the executable.</param>
    /// <param name="arguments">Arguments, one token each.</param>
    /// <param name="workingDirectory">The case directory.</param>
    /// <param name="environment">The complete environment of the process.</param>
    /// <param name="logPath">Where the merged output goes; null keeps the tail only.</param>
    /// <param name="cancellationToken">Kills the whole process tree when signaled.</param>
    /// <returns>The run's outcome.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The executable does not exist or cannot be started.</exception>
    public static async Task<FoamRunOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string? logPath,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(fileName, arguments, workingDirectory, environment);

        var tail = new Queue<string>(LOG_TAIL_LINES);
        await using var log = logPath == null
            ? null
            : new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var logLock = new object();

        void Append(string? line)
        {
            if (line == null)
                return;

            lock (logLock)
            {
                if (tail.Count == LOG_TAIL_LINES)
                    tail.Dequeue();
                tail.Enqueue(line);
                log?.WriteLine(line);
            }
        }

        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, args) => Append(args.Data);
        process.ErrorDataReceived += (_, args) => Append(args.Data);

        var stopwatch = Stopwatch.StartNew();

        process.Start();
        LowerPriority(process);
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
        lock (logLock)
        {
            logTail = string.Join('\n', tail);
        }

        return new FoamRunOutcome(process.ExitCode, stopwatch.Elapsed.TotalSeconds, logTail);
    }

    /// <summary>
    /// The start parameters of one process: no shell, no window, output
    /// redirected, the environment replaced whole.
    /// </summary>
    /// <remarks>
    /// <see cref="ProcessStartInfo.CreateNoWindow"/> matters on Windows: the
    /// worker client is a windowed application without a console, so without
    /// it every console program it starts gets a console of its own, at about
    /// 0.45 s per start (measured on the CalculiX controller).
    /// </remarks>
    /// <param name="fileName">Full path of the executable.</param>
    /// <param name="arguments">Arguments, one token each.</param>
    /// <param name="workingDirectory">The case directory.</param>
    /// <param name="environment">The complete environment of the process.</param>
    /// <returns>The start info <see cref="RunAsync"/> launches.</returns>
    public static ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment.Clear();
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;

        return startInfo;
    }

    /// <summary>
    /// Applies <see cref="RUN_PRIORITY"/> to a started process.
    /// </summary>
    /// <param name="process">The running process.</param>
    /// <returns>True when the priority was lowered.</returns>
    public static bool LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = RUN_PRIORITY;
            return true;
        }
        catch
        {
            // Not permitted here, or the process already exited; the run keeps normal priority.
            return false;
        }
    }

    #endregion
}
