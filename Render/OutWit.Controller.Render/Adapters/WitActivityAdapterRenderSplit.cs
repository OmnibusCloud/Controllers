using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Variables;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

/// <summary>
/// Adapter for <see cref="WitActivityRenderSplit"/>.
/// Generates a collection of <see cref="RenderTaskData"/> for distributed execution.
/// Host-side only. No Blender, no GPU — pure task generation.
/// </summary>
internal sealed class WitActivityAdapterRenderSplit :
    WitActivityAdapterFunction<WitActivityRenderSplit>
{
    #region Constructors

    public WitActivityAdapterRenderSplit(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityRenderSplit CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 4)
            throw new ArgumentException($"Render.Split expects 4 parameters, got {parameters.Length}");

        return new WitActivityRenderSplit
        {
            Scene = parameters[0],
            StartFrame = parameters[1],
            EndFrame = parameters[2],
            Options = parameters[3]
        };
    }

    protected override Task Process(
        WitActivityRenderSplit activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out Guid sceneId))
            throw new InvalidOperationException("Failed to get Blob parameter 'scene'");

        if (!pool.TryGetValue(activity.StartFrame, out int startFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'startFrame'");

        if (!pool.TryGetValue(activity.EndFrame, out int endFrame))
            throw new InvalidOperationException("Failed to get Int parameter 'endFrame'");

        if (!pool.TryGetValue(activity.Options, out RenderOptionsData? options) || options == null)
            throw new InvalidOperationException("Failed to get RenderOptions parameter 'options'");

        if (endFrame < startFrame)
            throw new InvalidOperationException(
                $"endFrame ({endFrame}) must be >= startFrame ({startFrame})");

        var tasks = new List<RenderTaskData>();
        int taskIndex = 0;

        for (int frame = startFrame; frame <= endFrame; frame++)
        {
            tasks.Add(new RenderTaskData
            {
                SceneBlobId = sceneId,
                Frame = frame,
                TileMinX = 0f,
                TileMaxX = 1f,
                TileMinY = 0f,
                TileMaxY = 1f,
                TaskIndex = taskIndex++,
                Options = (RenderOptionsData)options.Clone()
            });
        }

        Logger.LogInformation("Render.Split: generated {Count} frame tasks for frames {Start}-{End}",
            tasks.Count, startFrame, endFrame);

        pool.TrySetValue(activity.ReturnReference, tasks);

        return Task.CompletedTask;
    }

    #endregion
}
