using System.Collections;
using System.Linq;
using Microsoft.Extensions.Logging;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Parareal;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

internal sealed class WitActivityAdapterPararealCollect : WitActivityAdapterFunction<WitActivityPararealCollect>
{
    #region Constructors

    public WitActivityAdapterPararealCollect(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealCollect activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out PararealPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetObject(activity.Wave, out var waveObject) || waveObject is not IEnumerable waveEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Wave'.");

        if (!pool.TryGetValue(activity.State, out PararealStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        var wave = waveEnumerable.OfType<PararealResultData>().OrderBy(result => result.SlabIndex).ToList();
        if (wave.Count != plan.Slabs)
            throw new InvalidOperationException($"Parareal.Collect expected {plan.Slabs} wave results, got {wave.Count}.");

        for (var i = 0; i < wave.Count; i++)
        {
            // Count alone would let a duplicate+missing pair duplicate one slab's snapshots.
            if (wave[i].SlabIndex != i)
                throw new InvalidOperationException($"Parareal.Collect: wave is not a permutation of slabs (expected index {i}, got {wave[i].SlabIndex}).");
        }

        var timeline = new PararealTimeline
        {
            Convergence = new SimulationConvergenceInfo
            {
                Converged = state.Round > 0 && state.CorrectionNorm <= state.Eps * state.Scale,
                Iterations = state.Round,
                Eps = state.Eps,
                Scale = state.Scale
            }
        };
        timeline.Convergence.History.AddRange(state.History);

        foreach (var result in wave)
        {
            if (result.SnapshotPackBlobId == null)
                throw new InvalidOperationException($"Parareal.Collect: slab {result.SlabIndex} carries no snapshot pack — Collect consumes the final emit wave only.");

            var packPath = await BlobService.GetLocalPathAsync(result.SnapshotPackBlobId.Value);
            var pack = PararealSnapshotPack.FromBlobBytes(await File.ReadAllBytesAsync(packPath));

            if (timeline.Times.Count == 0)
                timeline.GlobalDims = (int[])pack.GlobalDims.Clone();

            timeline.Times.AddRange(pack.Times);
            timeline.Fields.AddRange(pack.Fields);
        }

        var timelineBlobId = await BlobService.UploadBytesAsync(timeline.ToBlobBytes(), "timeline.owsm");

        if (!pool.TrySetValue(activity.ReturnReference, timelineBlobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealCollect CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 3)
                throw new ArgumentException($"Expected 3 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference plan)
                throw new ArgumentException("Parameter 'Plan' must be a variable reference.");

            if (parameters[1] is not IWitReference wave)
                throw new ArgumentException("Parameter 'Wave' must be a variable reference.");

            if (parameters[2] is not IWitReference state)
                throw new ArgumentException("Parameter 'State' must be a variable reference.");

            return new WitActivityPararealCollect
            {
                Plan = plan,
                Wave = wave,
                State = state
            };
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to parse activity parameters.");
            throw;
        }
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
