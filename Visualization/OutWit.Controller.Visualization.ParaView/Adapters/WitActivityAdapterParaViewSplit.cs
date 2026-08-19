using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Activities;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Validation;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityParaViewSplit"/>: turns a valid validation report into the
/// deterministic task collection through <see cref="ParaViewTaskSplitter"/>. Refuses an invalid
/// report — a permanent input failure, never a retry.
/// </summary>
internal sealed class WitActivityAdapterParaViewSplit : WitActivityAdapterFunction<WitActivityParaViewSplit>
{
    #region Constructors

    public WitActivityAdapterParaViewSplit(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityParaViewSplit CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 3)
            throw new ArgumentException($"ParaView.Split expects 3 parameters (ParaViewSceneRef, ParaViewValidationReport, ParaViewOutputOptions), got {parameters.Length}");

        return new WitActivityParaViewSplit
        {
            Scene = parameters[0],
            Report = parameters[1],
            Options = parameters[2]
        };
    }

    protected override Task Process(
        WitActivityParaViewSplit activity,
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

        var tasks = ParaViewTaskSplitter.Split(scene, report, options);

        Logger.LogInformation("ParaView.Split: generated {Count} task(s) for view '{View}' (subset bytes min {Min}, max {Max}; {Fallbacks} fallback group(s))",
            tasks.Count, report.ResolvedViewId, tasks.Min(me => me.SubsetBytes), tasks.Max(me => me.SubsetBytes), report.Fallbacks.Count);

        if (!pool.TrySetValue(activity.ReturnReference, tasks.ToList()))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for ParaView.Split.");

        return Task.CompletedTask;
    }

    #endregion
}
