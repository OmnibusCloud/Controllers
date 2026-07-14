using System.Linq;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzInitRound : WitActivityAdapterFunction<WitActivitySchwarzInitRound>
{
    #region Constructors

    public WitActivityAdapterSchwarzInitRound(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzInitRound activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SchwarzPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        var state = new SchwarzRoundData
        {
            Round = 0,
            Residual = 0,
            InitialResidual = 0,
            Eps = plan.Eps,
            Boundaries = [.. Enumerable.Range(0, plan.Parts).Select(i => new SchwarzBoundarySetData { SubdomainIndex = i })]
        };

        if (!pool.TrySetValue(activity.ReturnReference, state))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzInitRound CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 1)
                throw new ArgumentException($"Expected 1 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            return new WitActivitySchwarzInitRound
            {
                Plan = plan
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion
}
