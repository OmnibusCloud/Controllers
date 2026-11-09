using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Adapters;

internal sealed class WitActivityAdapterCorpusDedup : WitActivityAdapterFunction<WitActivityCorpusDedup>
{
    #region Constants

    private const string BLOB_NAME = "corpus-dedup.json";

    #endregion

    #region Constructors

    public WitActivityAdapterCorpusDedup(IWitProcessingManager processingManager, IWitBlobService blobService, ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
    }

    #endregion

    #region Processing

    protected override async Task Process(WitActivityCorpusDedup activity, IWitVariablesCollection pool, IWitActivityStatus? activityStatus, WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Report, out Guid reportBlobId) || reportBlobId == Guid.Empty)
            throw new InvalidOperationException("Failed to get parameter 'Report'.");

        pool.TryGetValue(activity.Options, out CorpusOptionsData? options);
        var cancellationToken = ProcessingManager.CancellationToken(status.JobId);

        var shards = new List<CorpusDedupShard>();
        foreach (var (index, signaturesBlobId) in ReadSignatureBlobs(await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync(reportBlobId), cancellationToken)))
        {
            var bytes = await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync(signaturesBlobId), cancellationToken);
            shards.Add(new CorpusDedupShard(index, signaturesBlobId, ShardSignatureWriter.Read(bytes)));
        }

        var result = await Task.Run(() => new CorpusDeduplicator(options?.DedupThreshold ?? 0).Run(shards, cancellationToken), cancellationToken);
        Logger.LogInformation("Corpus.Dedup: {Documents} document(s) in {Shards} shard(s), {Duplicates} near-duplicate(s) in {Clusters} cluster(s) at threshold {Threshold}",
            result.Documents, result.Shards.Count, result.Duplicates, result.Clusters.Count, result.Threshold);

        var blobId = await BlobService.UploadBytesAsync(CorpusDedupWriter.Write(result), BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>The completed shards' signature blobs from a corpus report.</summary>
    /// <param name="report">The report bytes.</param>
    /// <returns>Shard index and blob id, in report order.</returns>
    /// <exception cref="InvalidOperationException">Not a corpus report.</exception>
    public static List<(int Index, Guid SignaturesBlobId)> ReadSignatureBlobs(byte[] report)
    {
        using var document = JsonDocument.Parse(report);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("schema").GetString() is not (CorpusReportWriter.SCHEMA or CorpusReportWriter.PREVIOUS_SCHEMA))
            throw new InvalidOperationException($"The report is not a {CorpusReportWriter.SCHEMA} document.");

        var blobs = new List<(int, Guid)>();
        foreach (var shard in root.GetProperty("shards").EnumerateArray())
        {
            if (shard.GetProperty("verdict").GetString() != CorpusShardVerdict.Completed.ToString())
                continue;

            if (Guid.TryParse(shard.GetProperty("signatures_blob").GetString(), out var blobId) && blobId != Guid.Empty)
                blobs.Add((shard.GetProperty("index").GetInt32(), blobId));
        }

        return blobs;
    }

    #endregion

    #region Parsing

    protected override WitActivityCorpusDedup CreateActivity(IWitParameter[] parameters)
    {
        try
        {
            if (parameters.Length != 2)
                throw new ArgumentException($"Expected 2 parameter(s), got {parameters.Length}.");

            if (parameters[0] is not IWitReference report)
                throw new ArgumentException("Parameter 'Report' must be a variable reference.");

            if (parameters[1] is not IWitReference options)
                throw new ArgumentException("Parameter 'Options' must be a variable reference.");

            return new WitActivityCorpusDedup
            {
                Report = report,
                Options = options
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
