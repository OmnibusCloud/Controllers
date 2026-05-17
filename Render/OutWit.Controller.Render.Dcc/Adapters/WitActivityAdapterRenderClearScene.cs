using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Dcc.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Adapters;

internal sealed class WitActivityAdapterRenderClearScene : WitActivityAdapterCommand<WitActivityRenderClearScene>
{
    #region Constructors

    public WitActivityAdapterRenderClearScene(
        IWitProcessingManager processingManager,
        ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Functions

    protected override WitActivityRenderClearScene CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new ArgumentException($"Render.ClearScene expects 1 parameter, got {parameters.Length}");

        if (parameters[0] is not IWitReference)
            throw new ArgumentException("Render.ClearScene expects a variable reference parameter");

        return new WitActivityRenderClearScene
        {
            Scene = parameters[0]
        };
    }

    protected override Task Process(
        WitActivityRenderClearScene activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (activity.Scene is not IWitReference sceneReference)
            throw new InvalidOperationException("Render.ClearScene requires a variable reference parameter");

        if (!pool.TryRemove(sceneReference.Reference))
            throw new InvalidOperationException($"Failed to remove DccScene parameter '{sceneReference.Reference}'");

        return Task.CompletedTask;
    }

    #endregion
}
