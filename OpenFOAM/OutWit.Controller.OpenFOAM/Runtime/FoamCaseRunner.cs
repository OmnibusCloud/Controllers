using Microsoft.Extensions.Logging;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Runs a recipe in a materialised case: step by step, each with the kit's
/// environment, its output in <c>log.&lt;utility&gt;</c>, the first failure
/// ending the run. Parallel steps go through the kit's MPI launcher with
/// <c>-parallel</c> appended; on a node without a launcher they run
/// serially and the decomposition steps are skipped, so the case still
/// produces a result there.
/// </summary>
public sealed class FoamCaseRunner
{
    #region Constants

    private static readonly IReadOnlySet<string> DECOMPOSITION_STEPS = new HashSet<string>(StringComparer.Ordinal)
    {
        "decomposePar", "reconstructPar", "reconstructParMesh"
    };

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
    public async Task<FoamRunReport> RunAsync(FoamRecipeData recipe, CancellationToken cancellationToken = default)
    {
        var parallel = Kit.SupportsParallel && Ranks > 1 && recipe.Steps.Any(step => step.Parallel);
        if (parallel)
            FoamDecomposition.WriteDecomposeParDict(CaseDirectory, Ranks);
        else if (recipe.Steps.Any(step => step.Parallel))
            Logger?.LogInformation("Foam.Run: no MPI launcher on this node ({Platform}) - the parallel steps run serially.", Kit.Platform);

        var report = new FoamRunReport();

        foreach (var step in recipe.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!parallel && DECOMPOSITION_STEPS.Contains(step.Utility))
            {
                report.Steps.Add(new FoamStepOutcomeData { Utility = step.Utility, Ranks = 0, ExitCode = 0, Seconds = 0 });
                continue;
            }

            var runParallel = parallel && step.Parallel;
            var (fileName, arguments) = CommandLine(step, runParallel);
            var logPath = Path.Combine(CaseDirectory, $"log.{step.Utility}");

            var outcome = await FoamProcessRunner.RunAsync(fileName, arguments, CaseDirectory, Environment, logPath, cancellationToken);

            report.Steps.Add(new FoamStepOutcomeData
            {
                Utility = step.Utility,
                Ranks = runParallel ? Ranks : 1,
                ExitCode = outcome.ExitCode,
                Seconds = outcome.ElapsedSeconds
            });

            if (outcome.ExitCode != 0)
            {
                report.FailedStep = step.Utility;
                report.ExitCode = outcome.ExitCode;
                report.LogTail = outcome.LogTail;
                break;
            }
        }

        return report;
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

    #endregion

    #region Properties

    private FoamKit Kit { get; }

    private string CaseDirectory { get; }

    private IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>Ranks the parallel steps run on.</summary>
    public int Ranks { get; }

    private ILogger? Logger { get; }

    #endregion
}
