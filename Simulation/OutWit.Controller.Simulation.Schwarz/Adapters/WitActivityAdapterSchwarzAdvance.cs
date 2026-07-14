using System.Collections;
using System.Linq;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Adapters;

internal sealed class WitActivityAdapterSchwarzAdvance : WitActivityAdapterFunction<WitActivitySchwarzAdvance>
{
    #region Constructors

    public WitActivityAdapterSchwarzAdvance(IWitProcessingManager processingManager, ILogger logger)
        : base(processingManager, logger)
    {
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySchwarzAdvance activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SchwarzPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out SchwarzRoundData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        if (!pool.TryGetObject(activity.Wave, out var waveObject) || waveObject is not IEnumerable waveEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Wave'.");

        // Wave results arrive in completion order — re-key by SubdomainIndex.
        var wave = waveEnumerable.OfType<SchwarzResultData>().OrderBy(result => result.SubdomainIndex).ToList();
        if (wave.Count != plan.Parts)
            throw new InvalidOperationException($"Schwarz.Advance expected {plan.Parts} wave results, got {wave.Count}.");

        for (var i = 0; i < wave.Count; i++)
        {
            // Count alone would let a duplicate+missing pair corrupt silently.
            if (wave[i].SubdomainIndex != i)
                throw new InvalidOperationException($"Schwarz.Advance: wave is not a permutation of subdomains (expected index {i}, got {wave[i].SubdomainIndex}).");
            if (wave[i].Round != state.Round)
                throw new InvalidOperationException($"Schwarz.Advance: subdomain {i} result is from round {wave[i].Round}, expected {state.Round}.");
        }

        var totalSquared = 0.0;
        foreach (var result in wave)
            totalSquared += result.ResidualSquared;

        var residual = Math.Sqrt(totalSquared);

        var next = new SchwarzRoundData
        {
            Round = state.Round + 1,
            Residual = residual,
            InitialResidual = state.Round == 0 ? residual : state.InitialResidual,
            Eps = state.Eps,
            Boundaries = [.. plan.Neighbors.Select(neighbors => new SchwarzBoundarySetData
            {
                SubdomainIndex = neighbors.SubdomainIndex,
                BlobIds = [.. neighbors.Producers.Select(producer => wave[producer].BoundaryOutBlobId)]
            })]
        };

        if (!pool.TrySetValue(activity.ReturnReference, next))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");

        await Task.CompletedTask;
    }

    #endregion

    #region Parsing

    protected override WitActivitySchwarzAdvance CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 3)
                throw new ArgumentException($"Expected 3 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            if (parameters[2] is not IWitReference wave)
                throw new ArgumentException("Parameter 'Wave' must be a variable reference.");

            return new WitActivitySchwarzAdvance
            {
                Plan = plan,
                State = state,
                Wave = wave
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
