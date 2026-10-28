using Microsoft.Extensions.Logging;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Runs a recipe in a materialised case: step by step, each with the kit's
/// environment, its output in <c>log.&lt;utility&gt;</c>, the first failure
/// ending the run. Parallel steps go through the kit's MPI launcher with
/// <c>-parallel</c> appended; on a node without a launcher they run
/// serially and the decomposition steps are skipped, so the case still
/// produces a result there. The controller's own steps run in-process, with a
/// log like any step's: <c>restore0Dir -processor</c> through
/// <see cref="FoamInitialFields"/>, <c>includeFunc &lt;response&gt;</c>
/// through <see cref="FoamSolveFunctions"/>.
/// </summary>
public sealed class FoamCaseRunner
{
    #region Constants

    private static readonly IReadOnlySet<string> DECOMPOSITION_STEPS = new HashSet<string>(StringComparer.Ordinal)
    {
        "decomposePar", "reconstructPar", "reconstructParMesh"
    };

    private const string POST_PROCESS = "postProcess";

    private const string POST_PROCESS_FLAG = "-postProcess";

    private const string FUNCTION_FLAG = "-func";

    #endregion

    #region Constructors

    /// <summary>
    /// A runner for one case.
    /// </summary>
    /// <param name="kit">The kit.</param>
    /// <param name="caseDirectory">The materialised case.</param>
    /// <param name="environment">The process environment (KIT.env resolved for this task's scratch).</param>
    /// <param name="ranks">Ranks for the parallel steps.</param>
    /// <param name="logger">Diagnostics sink.</param>
    public FoamCaseRunner(FoamKit kit, string caseDirectory, IReadOnlyDictionary<string, string> environment, int ranks, ILogger? logger = null)
    {
        Kit = kit;
        CaseDirectory = caseDirectory;
        Environment = environment;
        Ranks = Math.Max(1, ranks);
        Logger = logger;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs the recipe.
    /// </summary>
    /// <param name="recipe">A validated recipe.</param>
    /// <param name="cancellationToken">Kills the running step's process tree when signaled.</param>
    /// <returns>Every step's outcome, and the failure if there was one.</returns>
    public Task<FoamRunReport> RunAsync(FoamRecipeData recipe, CancellationToken cancellationToken = default)
    {
        return RunAsync(recipe, [], cancellationToken);
    }

    /// <summary>
    /// Runs the recipe, with the named responses added to the solve by the
    /// controller before the first step that runs the application to solve:
    /// one <c>includeFunc &lt;response&gt;</c> step of the controller's own per
    /// name, in the report and with a log like any step's. Why: a post step
    /// (<c>postProcess -func</c>, <c>-latestTime</c>) reads the latest written
    /// time, which is not the run's final time when <c>endTime</c> is not a
    /// multiple of <c>writeInterval</c> - the response then came back blank
    /// (measured on v2606, 2026-10-28); measured by the solver at every step,
    /// its last row is the final time's.
    /// </summary>
    /// <param name="recipe">A validated recipe.</param>
    /// <param name="measuredInSolve">The responses to add to the solve, by name (<see cref="FoamSolveResponses"/>); each is a dictionary in <c>system/</c> the controller wrote.</param>
    /// <param name="cancellationToken">Kills the running step's process tree when signaled.</param>
    /// <returns>Every step's outcome, the controller's own included, and the failure if there was one.</returns>
    public async Task<FoamRunReport> RunAsync(FoamRecipeData recipe, IReadOnlyList<string> measuredInSolve, CancellationToken cancellationToken = default)
    {
        var parallel = Kit.SupportsParallel && Ranks > 1 && recipe.Steps.Any(step => step.Parallel);
        if (parallel)
            FoamDecomposition.WriteDecomposeParDict(CaseDirectory, Ranks);
        else if (recipe.Steps.Any(step => step.Parallel))
            Logger?.LogInformation("Foam.Run: no MPI launcher on this node ({Platform}) - the parallel steps run serially.", Kit.Platform);

        // A restore into the processor directories restores the fields as they were before any step ran.
        if (parallel && recipe.Steps.Any(step => step.Utility == FoamAllowList.RESTORE_INITIAL_FIELDS))
            FoamInitialFields.Keep(CaseDirectory);

        var report = new FoamRunReport();
        var logCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var pending = measuredInSolve.ToList();

        foreach (var step in recipe.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!parallel && DECOMPOSITION_STEPS.Contains(step.Utility))
            {
                report.Steps.Add(new FoamStepOutcomeData { Utility = step.Utility, Ranks = 0, ExitCode = 0, Seconds = 0 });
                continue;
            }

            // The controller's own includes go right before the first solve,
            // where the recipe's own includeFunc steps stand.
            if (pending.Count > 0 && IsSolve(step, recipe.Application))
            {
                var added = true;
                foreach (var response in pending)
                {
                    var include = new FoamStepData { Utility = FoamAllowList.INCLUDE_FUNCTION, Arguments = [response] };
                    var includeLog = Path.Combine(CaseDirectory, LogName(include.Utility, logCounts));
                    added &= Record(report, include, FoamSolveFunctions.Include(CaseDirectory, response, includeLog, FoamSolveFunctions.WHY_FINAL_TIME), 0, includeLog);
                    if (!added)
                        break;
                }

                pending.Clear();
                if (!added)
                    break;
            }

            var runParallel = parallel && step.Parallel;
            var builtIn = FoamAllowList.IsBuiltIn(step.Utility);
            var logPath = Path.Combine(CaseDirectory, LogName(step.Utility, logCounts));

            // A post step for a response the solver measured is not run: it
            // would read the latest written time only, and for a probe it
            // overwrites the solve's rows with that one (measured on v2606,
            // 2026-10-28: postProcessing/<probe>/0/p kept one row, at 0.04).
            var measured = PostStepResponse(step, recipe.Application, measuredInSolve);
            var outcome = measured != null
                ? SkipPostStep(step, measured, logPath)
                : builtIn
                    ? RunBuiltIn(step, parallel, logPath)
                    : await RunProcessAsync(step, runParallel, logPath, cancellationToken);

            if (!Record(report, step, outcome, builtIn || measured != null ? 0 : runParallel ? Ranks : 1, logPath))
                break;
        }

        return report;
    }

    /// <summary>
    /// The response a post step measures (<c>postProcess -func &lt;response&gt;</c>
    /// or <c>&lt;application&gt; -postProcess -func &lt;response&gt;</c>) when it is one
    /// the controller measured in the solve; null for any other step.
    /// </summary>
    /// <param name="step">A recipe step.</param>
    /// <param name="application">The recipe's application.</param>
    /// <param name="measuredInSolve">The responses the controller added to the solve.</param>
    /// <returns>The response's name, or null.</returns>
    public static string? PostStepResponse(FoamStepData step, string application, IReadOnlyList<string> measuredInSolve)
    {
        if (measuredInSolve.Count == 0)
            return null;

        var isPostStep = step.Utility == POST_PROCESS || (step.Utility == application && step.Arguments.Contains(POST_PROCESS_FLAG));
        if (!isPostStep)
            return null;

        var function = step.Arguments.IndexOf(FUNCTION_FLAG);
        if (function < 0 || function + 1 >= step.Arguments.Count)
            return null;

        var response = step.Arguments[function + 1];
        return measuredInSolve.Contains(response, StringComparer.Ordinal) ? response : null;
    }

    private static FoamRunOutcome SkipPostStep(FoamStepData step, string response, string logPath)
    {
        var text = $"Not run: '{step.Utility} {string.Join(' ', step.Arguments)}'. The solver measured '{response}' at every step (the controller's includeFunc), " +
                   "so its last row is the final time's; a post step reads the latest written time only, and for a probe it overwrites the solve's rows with that one.\n";
        File.WriteAllText(logPath, text);
        return new FoamRunOutcome(0, 0, text);
    }

    /// <summary>Adds a step's outcome to the report; false, with the failure recorded, when the step failed.</summary>
    private static bool Record(FoamRunReport report, FoamStepData step, FoamRunOutcome outcome, int ranks, string logPath)
    {
        report.Add(step, new FoamStepOutcomeData
        {
            Utility = step.Utility,
            Ranks = ranks,
            ExitCode = outcome.ExitCode,
            Seconds = outcome.ElapsedSeconds
        }, logPath);

        if (outcome.ExitCode == 0)
            return true;

        report.FailedStep = step.Utility;
        report.ExitCode = outcome.ExitCode;
        report.LogTail = outcome.LogTail;
        return false;
    }

    /// <summary>A step that runs the application to solve, not its <c>-postProcess</c> form.</summary>
    private static bool IsSolve(FoamStepData step, string application)
    {
        return step.Utility == application && !step.Arguments.Contains("-postProcess");
    }

    private FoamRunOutcome RunBuiltIn(FoamStepData step, bool decomposed, string logPath)
    {
        return step.Utility == FoamAllowList.INCLUDE_FUNCTION
            ? FoamSolveFunctions.Include(CaseDirectory, step.Arguments[0], logPath)
            : FoamInitialFields.RestoreIntoProcessors(CaseDirectory, logPath, decomposed);
    }

    private Task<FoamRunOutcome> RunProcessAsync(FoamStepData step, bool parallel, string logPath, CancellationToken cancellationToken)
    {
        var (fileName, arguments) = CommandLine(step, parallel);
        return FoamProcessRunner.RunAsync(fileName, arguments, CaseDirectory, Environment, logPath, cancellationToken);
    }

    /// <summary>
    /// The command line of one step: the executable and its arguments, or the
    /// MPI launcher with the executable and <c>-parallel</c> for a parallel step.
    /// </summary>
    /// <param name="step">The step.</param>
    /// <param name="parallel">True to launch under MPI.</param>
    /// <returns>File name and argument list.</returns>
    public (string FileName, IReadOnlyList<string> Arguments) CommandLine(FoamStepData step, bool parallel)
    {
        var executable = Kit.ExecutablePath(step.Utility);

        if (!parallel || Kit.MpiLauncher == null)
            return (executable, step.Arguments.ToList());

        var arguments = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            arguments.Add("-n");
            arguments.Add(Ranks.ToString());
        }
        else
        {
            arguments.Add("-np");
            arguments.Add(Ranks.ToString());
        }

        arguments.Add(executable);
        arguments.AddRange(step.Arguments);
        arguments.Add("-parallel");

        return (Kit.MpiLauncher, arguments);
    }

    /// <summary>
    /// The log file name of a step: OpenFOAM's own <c>log.&lt;utility&gt;</c>
    /// for the first run of a utility, <c>log.&lt;utility&gt;.2</c> and so on
    /// for later runs of the same one - a solver followed by its
    /// <c>-postProcess</c> form keeps both logs, and the solve's facts are
    /// read from the solve's.
    /// </summary>
    /// <param name="utility">The step's utility.</param>
    /// <param name="counts">How many times each utility has run so far; updated.</param>
    /// <returns>The file name, relative to the case.</returns>
    public static string LogName(string utility, Dictionary<string, int> counts)
    {
        counts.TryGetValue(utility, out var seen);
        counts[utility] = seen + 1;

        return seen == 0 ? $"log.{utility}" : $"log.{utility}.{seen + 1}";
    }

    #endregion

    #region Properties

    /// <summary>Ranks the parallel steps run on.</summary>
    public int Ranks { get; }

    private FoamKit Kit { get; }

    private string CaseDirectory { get; }

    private IReadOnlyDictionary<string, string> Environment { get; }

    private ILogger? Logger { get; }

    #endregion
}
