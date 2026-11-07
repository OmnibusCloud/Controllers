using System.Collections.Concurrent;
using System.Reflection;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Utils;

/// <summary>
/// Reports whole-job progress to the host that runs the job's script, through the engine's
/// job-level sink <c>ReportJobProgress(Guid jobId, double fraction, string? stage)</c>.
/// The sink is not part of <see cref="IWitProcessingManager"/>, so it is looked up by reflection
/// on the injected instance: on a host that predates it (or in the SDK) the lookup finds nothing
/// and reporting is silently skipped, and the controller stays loadable everywhere.
/// </summary>
public static class JobProgressReporter
{
    #region Constants

    private const string REPORT_JOB_PROGRESS = "ReportJobProgress";

    #endregion

    #region Static Fields

    private static readonly ConcurrentDictionary<Type, MethodInfo?> REPORT_METHODS = new();

    #endregion

    #region Functions

    /// <summary>
    /// Sends one progress report for <paramref name="jobId"/>. Never throws: progress is purely
    /// informational and must not disturb the solve.
    /// </summary>
    /// <param name="manager">The processing manager injected into the reporting adapter.</param>
    /// <param name="jobId">The job whose progress is reported.</param>
    /// <param name="fraction">How far the job is towards completion, 0..1.</param>
    /// <param name="stage">A short human-readable description of what the job is doing now.</param>
    /// <returns><c>true</c> when the host accepted the call; <c>false</c> when it has no job-level sink.</returns>
    public static bool Report(IWitProcessingManager? manager, Guid jobId, double fraction, string? stage)
    {
        if (manager == null)
            return false;

        var method = REPORT_METHODS.GetOrAdd(
            manager.GetType(),
            static type => type.GetMethod(REPORT_JOB_PROGRESS, [typeof(Guid), typeof(double), typeof(string)]));

        if (method == null)
            return false;

        try
        {
            method.Invoke(manager, [jobId, fraction, stage]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion
}
