using System.Collections;
using System.Linq;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Numerics;
using OutWit.Controller.Simulation.Model.Parareal;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Controller.Simulation.Parareal.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Adapters;

internal sealed class WitActivityAdapterPararealCorrect : WitActivityAdapterFunction<WitActivityPararealCorrect>
{
    #region Constructors

    public WitActivityAdapterPararealCorrect(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityPararealCorrect activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out PararealPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out PararealStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        if (!pool.TryGetObject(activity.Wave, out var waveObject) || waveObject is not IEnumerable waveEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Wave'.");

        var frontier = Math.Min(state.Round, plan.Slabs - 1);

        // Wave results arrive in completion order — re-key by SlabIndex.
        var wave = waveEnumerable.OfType<PararealResultData>().OrderBy(result => result.SlabIndex).ToList();
        if (wave.Count != plan.Slabs - frontier)
            throw new InvalidOperationException($"Parareal.Correct expected {plan.Slabs - frontier} wave results, got {wave.Count}.");

        for (var i = 0; i < wave.Count; i++)
        {
            // Count alone would let a duplicate+missing pair corrupt silently.
            if (wave[i].SlabIndex != frontier + i)
                throw new InvalidOperationException($"Parareal.Correct: wave is not a permutation of active slabs (expected index {frontier + i}, got {wave[i].SlabIndex}).");
        }

        var kernel = await PararealKernelCache.GetOrCreateAsync(BlobService, plan);

        var oldFields = new double[plan.Slabs + 1][];
        for (var slab = 0; slab <= plan.Slabs; slab++)
            oldFields[slab] = await DownloadFieldAsync(state.StateBlobIds[slab]);

        var waveFields = new double[plan.Slabs][];
        foreach (var result in wave)
            waveFields[result.SlabIndex] = await DownloadFieldAsync(result.StateOutBlobId);

        var newFields = new double[plan.Slabs + 1][];
        var newBlobIds = new List<Guid>(plan.Slabs + 1);
        for (var slab = 0; slab <= frontier; slab++)
        {
            // Converged prefix: same immutable blobs, same field arrays — the
            // G(new) − G(old) terms below cancel bitwise (the exact-slab trick).
            newFields[slab] = oldFields[slab];
            newBlobIds.Add(state.StateBlobIds[slab]);
        }

        for (var slab = frontier; slab < plan.Slabs; slab++)
        {
            var coarseNew = kernel.CoarsePropagate(newFields[slab]);
            var coarseOld = kernel.CoarsePropagate(oldFields[slab]);

            var next = new double[coarseNew.Length];
            for (var i = 0; i < next.Length; i++)
                next[i] = waveFields[slab][i] + (coarseNew[i] - coarseOld[i]);

            newFields[slab + 1] = next;

            var snapshot = new PararealStateSnapshot
            {
                SlabIndex = slab + 1,
                Round = state.Round + 1,
                Time = plan.SlabBoundaries[slab + 1],
                GlobalDims = (int[])kernel.FineProblem.Geometry.Dims.Clone(),
                Values = next
            };
            newBlobIds.Add(await BlobService.UploadBytesAsync(snapshot.ToBlobBytes(), $"state_s{slab + 1}_r{state.Round + 1}.owsm"));
        }

        var correction = 0.0;
        for (var slab = frontier + 1; slab <= plan.Slabs; slab++)
            correction = Math.Max(correction, FixedOrderNorms.MaxAbsDifference(newFields[slab], oldFields[slab]));

        var next2 = new PararealStateData
        {
            Round = state.Round + 1,
            CorrectionNorm = correction,
            Scale = state.Scale,
            Eps = state.Eps,
            StateBlobIds = newBlobIds,
            History = [.. state.History, correction]
        };

        if (!pool.TrySetValue(activity.ReturnReference, next2))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    private async Task<double[]> DownloadFieldAsync(Guid blobId)
    {
        var path = await BlobService.GetLocalPathAsync(blobId);
        return PararealStateSnapshot.FromBlobBytes(await File.ReadAllBytesAsync(path)).Values;
    }

    #endregion

    #region Parsing

    protected override WitActivityPararealCorrect CreateActivity(IWitParameter[] parameters)
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

            return new WitActivityPararealCorrect
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

    #region Properties

    private IWitBlobService BlobService { get; }

    #endregion
}
