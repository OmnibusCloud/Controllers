using System.Diagnostics;

namespace OutWit.Controller.CalculiX.Runtime;

/// <summary>
/// Spawns the real ccx with its host contract: bare jobname argument (no -i),
/// cwd = the job directory, OMP_NUM_THREADS set explicitly (and the equation
/// solver's own thread count where it must differ, see
/// <see cref="CcxEquationSolver"/>), kill propagated to the whole process tree
/// on cancellation, exit code forwarded verbatim.
/// </summary>
public static class CcxProcessRunner
{
    #region Constants

    private const int LOG_TAIL_LINES = 60;

    /// <summary>
    /// The OpenMP thread count a solve gets when the caller asks for "all cores": the logical
    /// cores of the machine, capped here. PARDISO on the decks this controller runs saturates
    /// at eight to sixteen threads; on a 32-thread desktop the SMT siblings cost 7-12 %
    /// (Ryzen 9 5950X, 2026-09-22: 0.67 s at 8, 0.75 s at 32 on the reference cube; 8.4 s vs
    /// 9.0 s on a 64k-node cube).
    /// </summary>
    public const int MAX_DEFAULT_THREADS = 16;

    /// <summary>
    /// The priority a solve runs at. A worker is often somebody's desktop: a solve or the
    /// benchmark (35-70 s on the 40-cube) takes every core it is given, and at normal priority
    /// the person at the keyboard waits behind it. Below normal, the interactive programs get
    /// the CPU first and the solve takes what is left - all of it on an idle machine, so the
    /// rate does not change there. Best-effort: a platform that refuses keeps normal.
    /// </summary>
    public const ProcessPriorityClass SOLVE_PRIORITY = ProcessPriorityClass.BelowNormal;

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
    public static Task<CcxRunOutcome> RunAsync(
        string solverPath,
        string jobName,
        string jobDirectory,
        int threads,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(solverPath, jobName, jobDirectory, threads, equationSolverThreads: null, cancellationToken);
    }

    /// <summary>
    /// Runs one solve to completion, with the equation solver's own thread count.
    /// </summary>
    /// <param name="solverPath">Full path of the ccx executable.</param>
    /// <param name="jobName">Bare job name; ccx reads &lt;jobName&gt;.inp in the job directory.</param>
    /// <param name="jobDirectory">Directory holding the deck; results land here.</param>
    /// <param name="threads">OMP thread count; 0 = all cores of this machine.</param>
    /// <param name="equationSolverThreads">The equation solver's thread count
    /// (<see cref="CcxEquationSolver.THREADS_VARIABLE"/>), or null to leave it to OpenMP.</param>
    /// <param name="cancellationToken">Kills the whole solver process tree when signaled.</param>
    /// <returns>The run's outcome.</returns>
    public static async Task<CcxRunOutcome> RunAsync(
        string solverPath,
        string jobName,
        string jobDirectory,
        int threads,
        int? equationSolverThreads,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(solverPath, jobName, jobDirectory, threads, equationSolverThreads);

        var tail = new Queue<string>(LOG_TAIL_LINES);

        using var process = new Process();
        process.StartInfo = startInfo;
        process.OutputDataReceived += (_, args) => AppendLine(tail, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(tail, args.Data);

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
        lock (tail)
        {
            logTail = string.Join('\n', tail);
        }

        return new CcxRunOutcome(process.ExitCode, stopwatch.Elapsed.TotalSeconds, logTail);
    }

    /// <summary>
    /// Applies <see cref="SOLVE_PRIORITY"/> to a started solver process.
    /// </summary>
    /// <param name="process">The running ccx process.</param>
    /// <returns>True when the priority was lowered.</returns>
    public static bool LowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = SOLVE_PRIORITY;
            return true;
        }
        catch
        {
            // Not permitted here, or the process already exited; the solve runs at normal priority.
            return false;
        }
    }

    /// <summary>
    /// The thread count "all cores" resolves to on this machine (see <see cref="MAX_DEFAULT_THREADS"/>).
    /// </summary>
    /// <returns>The logical core count, at most the cap, at least one.</returns>
    public static int DefaultThreads()
    {
        return Math.Clamp(Environment.ProcessorCount, 1, MAX_DEFAULT_THREADS);
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
    /// <returns>The start info <see cref="RunAsync(string, string, string, int, CancellationToken)"/> launches.</returns>
    public static ProcessStartInfo CreateStartInfo(string solverPath, string jobName, string jobDirectory, int threads)
    {
        return CreateStartInfo(solverPath, jobName, jobDirectory, threads, equationSolverThreads: null);
    }

    /// <summary>
    /// The start parameters of one solve, with the equation solver's own thread count.
    /// </summary>
    /// <param name="solverPath">Full path of the ccx executable.</param>
    /// <param name="jobName">Bare job name; ccx reads &lt;jobName&gt;.inp in the job directory.</param>
    /// <param name="jobDirectory">Directory holding the deck; results land here.</param>
    /// <param name="threads">OMP thread count; 0 = all cores of this machine.</param>
    /// <param name="equationSolverThreads">The equation solver's thread count
    /// (<see cref="CcxEquationSolver.THREADS_VARIABLE"/>), or null to leave it to OpenMP.</param>
    /// <returns>The start info.</returns>
    public static ProcessStartInfo CreateStartInfo(string solverPath, string jobName, string jobDirectory, int threads, int? equationSolverThreads)
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
            (threads > 0 ? threads : DefaultThreads()).ToString();

        // Always set or cleared: a value inherited from the node's environment must not decide it.
        startInfo.EnvironmentVariables.Remove(CcxEquationSolver.THREADS_VARIABLE);
        if (equationSolverThreads is > 0)
            startInfo.EnvironmentVariables[CcxEquationSolver.THREADS_VARIABLE] = equationSolverThreads.Value.ToString();

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
