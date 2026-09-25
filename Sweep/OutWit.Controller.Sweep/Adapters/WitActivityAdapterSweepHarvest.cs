using System.Collections;
using MemoryPack;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Families;
using OutWit.Controller.Sweep.Model;
using OutWit.Controller.Sweep.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Adapters;

internal sealed class WitActivityAdapterSweepHarvest : WitActivityAdapterFunction<WitActivitySweepHarvest>
{
    #region Constructors

    public WitActivityAdapterSweepHarvest(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivitySweepHarvest activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Plan, out SweepPlanData? plan) || plan?.Options == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out SweepStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        if (!pool.TryGetObject(activity.Wave, out var waveObject) || waveObject is not IEnumerable wave)
            throw new InvalidOperationException("Failed to get parameter 'Wave'.");

        var family = SweepFamilies.Of(plan);
        var rows = family.ToRows(wave);

        // Grid delivers the chunk as one group, so the wave is exactly the
        // chunk's variants, each once - a short wave (a protocol drift, a
        // foreign entry the family left out), a variant twice or one from
        // outside the chunk must fail LOUDLY here, because the silent
        // alternative is a manifest that miscounts forever.
        var expected = plan.ChunkSizes[state.ChunkIndex];
        var chunk = plan.Options.Variants
            .Skip(state.NextVariantOrdinal)
            .Take(expected)
            .Select(variant => variant.VariantIndex)
            .ToList();
        var findings = SweepWaveCheck.Findings(chunk, rows.Select(row => row.VariantIndex).ToList());
        if (findings.Count > 0)
            throw new InvalidOperationException(
                $"Chunk {state.ChunkIndex} returned {rows.Count} {family.Family} result(s) for {expected} task(s): {string.Join("; ", findings)}.");

        var manifest = await DownloadManifestAsync(state.ManifestBlobId);
        manifest.Rows.AddRange(rows);
        // Results arrive in completion order; the manifest keeps them sorted
        // by variant for readable diffs between polls.
        manifest.Rows = manifest.Rows.OrderBy(row => row.VariantIndex).ToList();

        var manifestBlobId = await BlobService.UploadBytesAsync(
            MemoryPackSerializer.Serialize(manifest),
            $"manifest_{state.ChunkIndex}.bin");

        var advanced = new SweepStateData
        {
            ChunkIndex = state.ChunkIndex + 1,
            NextVariantOrdinal = state.NextVariantOrdinal + expected,
            SucceededCount = state.SucceededCount + rows.Count(row => row.Outcome == SweepOutcome.Succeeded),
            FailedCount = state.FailedCount + rows.Count(row => row.Outcome == SweepOutcome.Failed),
            RefusedCount = state.RefusedCount + rows.Count(row => row.Outcome == SweepOutcome.Refused),
            ManifestBlobId = manifestBlobId,
            // The document-client view of the manifest: the cumulative index
            // rides the state variable, which the door renders as
            // sweep.state@2 - the manifest blob itself stays MemoryPack.
            Results = manifest.Rows
                .Select(row => new SweepResultIndexEntryData
                {
                    VariantIndex = row.VariantIndex,
                    Outcome = row.Outcome,
                    Label = SweepVariantLabel.Of(plan.Options, row.VariantIndex),
                    Artifacts = family.ArtifactsOf(row).ToList()
                })
                .ToList()
        };

        if (!pool.TrySetValue(activity.ReturnReference, advanced))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    private async Task<SweepManifestData> DownloadManifestAsync(Guid? manifestBlobId)
    {
        if (manifestBlobId == null)
            return new SweepManifestData();

        var path = await BlobService.GetLocalPathAsync(manifestBlobId.Value);
        return MemoryPackSerializer.Deserialize<SweepManifestData>(await File.ReadAllBytesAsync(path))
               ?? new SweepManifestData();
    }

    #endregion

    #region Parsing

    protected override WitActivitySweepHarvest CreateActivity(IWitParameter[] parameters)
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

            return new WitActivitySweepHarvest
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
