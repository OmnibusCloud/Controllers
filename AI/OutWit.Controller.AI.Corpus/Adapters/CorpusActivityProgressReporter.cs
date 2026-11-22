using System.Collections.Concurrent;
using System.Reflection;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

/// <summary>
/// Builds a reporter that forwards a node activity's live 0..1 fraction to the host's
/// <c>ProcessingManager.ReportActivityProgress(Guid, double, string?)</c> — by reflection on
/// purpose (the Render precedent): the method ships with Engine.Shared 1.1.6+, and a reflective
/// lookup keeps the controller loadable on an older host, where the lookup finds nothing and
/// progress is simply not reported. The SDK's manager used by tests has no such method either.
/// </summary>
internal static class CorpusActivityProgressReporter
{
    #region Constants

    private const string METHOD_NAME = "ReportActivityProgress";

    #endregion

    #region Fields

    private static readonly ConcurrentDictionary<Type, MethodInfo?> METHODS = new();

    #endregion

    #region Functions

    /// <summary>
    /// Returns a reporter for the job, or null when the host cannot receive activity progress.
    /// </summary>
    /// <param name="manager">The adapter's processing manager.</param>
    /// <param name="jobId">The job the activity belongs to.</param>
    /// <returns>A <c>(fraction, stage)</c> callback that never throws, or null.</returns>
    public static Action<double, string>? Create(IWitProcessingManager? manager, Guid jobId)
    {
        if (manager == null)
            return null;

        var method = METHODS.GetOrAdd(
            manager.GetType(),
            static type => type.GetMethod(METHOD_NAME, [typeof(Guid), typeof(double), typeof(string)]));

        if (method == null)
            return null;

        return (fraction, stage) =>
        {
            try
            {
                method.Invoke(manager, [jobId, fraction, stage]);
            }
            catch
            {
                // Progress is informational; it must never disturb the shard.
            }
        };
    }

    #endregion
}
