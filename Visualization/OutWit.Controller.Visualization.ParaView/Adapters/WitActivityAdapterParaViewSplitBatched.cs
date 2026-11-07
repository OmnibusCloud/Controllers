using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Activities;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Tasks;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityParaViewSplitBatched"/>: the deterministic tasks of
/// <see cref="ParaViewTaskSplitter"/> grouped into chunks by <see cref="ParaViewChunkPolicy"/>, each
/// chunk carrying the union of its outputs' attachment subsets. Refuses an invalid report — a
/// permanent input failure, never a retry.
/// </summary>
internal sealed class WitActivityAdapterParaViewSplitBatched : WitActivityAdapterFunction<WitActivityParaViewSplitBatched>
{
    #region Constructors

    public WitActivityAdapterParaViewSplitBatched(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityParaViewSplitBatched CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException($"ParaView.SplitBatched expects 3 parameters (ParaViewSceneRef, ParaViewValidationReport, ParaViewOutputOptions), got {parameters.Length}");

        return new WitActivityParaViewSplitBatched
        {
            Scene = parameters[0],
            Report = parameters[1],
            Options = parameters[2]
        };
    }

    protected override Task Process(
        WitActivityParaViewSplitBatched activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out ParaViewSceneRefData? scene) || scene == null)
            throw new InvalidOperationException("Failed to get ParaViewSceneRef parameter 'scene'");

        if (!pool.TryGetValue(activity.Report, out ParaViewValidationReportData? report) || report == null)
            throw new InvalidOperationException("Failed to get ParaViewValidationReport parameter 'report'");

        if (!pool.TryGetValue(activity.Options, out ParaViewOutputOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get ParaViewOutputOptions parameter 'options'");

        var batches = ParaViewTaskSplitter.SplitBatched(scene, report, options);

        Logger.LogInformation("ParaView.SplitBatched: generated {Outputs} output(s) in {Batches} batch(es) of up to {Chunk} for view '{View}' (batch bytes min {Min}, max {Max}; {Fallbacks} fallback group(s))",
            batches.Sum(me => me.Tasks.Count), batches.Count, batches.Max(me => me.Tasks.Count), report.ResolvedViewId,
            batches.Min(me => me.SubsetBytes), batches.Max(me => me.SubsetBytes), report.Fallbacks.Count);

        if (!pool.TrySetValue(activity.ReturnReference, batches.ToList()))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.SplitBatched.");

        return Task.CompletedTask;
    }

    #endregion
}
