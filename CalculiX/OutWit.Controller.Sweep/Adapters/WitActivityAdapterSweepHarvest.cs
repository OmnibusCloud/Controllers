using System.Collections;
using MemoryPack;
using Microsoft.Extensions.Logging;
using OutWit.Controller.CalculiX.Model;
using OutWit.Controller.Sweep.Activities;
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
        if (!pool.TryGetValue(activity.Plan, out SweepPlanData? plan) || plan == null)
            throw new InvalidOperationException("Failed to get parameter 'Plan'.");

        if (!pool.TryGetValue(activity.State, out SweepStateData? state) || state == null)
            throw new InvalidOperationException("Failed to get parameter 'State'.");

        if (!pool.TryGetObject(activity.Wave, out var waveObject) || waveObject is not IEnumerable waveEnumerable)
            throw new InvalidOperationException("Failed to get parameter 'Wave'.");

        // Wave results arrive in completion order — the manifest keeps them
        // sorted by variant for readable diffs between polls.
        var results = waveEnumerable
            .OfType<CcxResultData>()
            .OrderBy(result => result.VariantIndex)
            .ToList();

        var manifest = await DownloadManifestAsync(state.ManifestBlobId);
        foreach (var result in results)
        {
            manifest.Rows.Add(new SweepManifestRowData
            {
                VariantIndex = result.VariantIndex,
                Succeeded = result.ExitCode == 0,
                ExitCode = result.ExitCode,
                SolveSeconds = result.SolveSeconds,
                FrdBlobId = result.FrdBlobId,
                DatBlobId = result.DatBlobId,
                ResponseRow = result.ResponseRow,
                LogTail = result.LogTail
            });
        }

        var manifestBlobId = await BlobService.UploadBytesAsync(
            MemoryPackSerializer.Serialize(manifest),
            $"manifest_{state.ChunkIndex}.bin");

        var advanced = new SweepStateData
        {
            ChunkIndex = state.ChunkIndex + 1,
            NextVariantOrdinal = state.NextVariantOrdinal + plan.ChunkSizes[state.ChunkIndex],
            CompletedCount = state.CompletedCount + results.Count(result => result.ExitCode == 0),
            FailedCount = state.FailedCount + results.Count(result => result.ExitCode != 0),
            ManifestBlobId = manifestBlobId
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
