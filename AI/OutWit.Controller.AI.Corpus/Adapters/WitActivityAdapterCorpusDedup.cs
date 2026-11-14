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

        // A run without signatures, or with signatures made in different ways, is refused by name.
        var input = CorpusDedupInput.Read(await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync(reportBlobId), cancellationToken));
        if (input.Refusal != null)
            throw new InvalidOperationException($"Corpus.Dedup: {input.Refusal}");

        var shards = new List<CorpusDedupShard>();
        foreach (var source in input.Shards)
        {
            var bytes = await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync(source.SignaturesBlobId), cancellationToken);
            if (CorpusDedupInput.Check(source, bytes) is { } mismatch)
                throw new InvalidOperationException($"Corpus.Dedup: {mismatch}");

            shards.Add(new CorpusDedupShard(source.Index, source.SignaturesBlobId, ShardSignatureWriter.Read(bytes)));
        }

        var result = await Task.Run(() => new CorpusDeduplicator(options?.DedupThreshold ?? 0, input.Parameters.Bands).Run(shards, cancellationToken), cancellationToken);
        Logger.LogInformation("Corpus.Dedup: {Documents} document(s) in {Shards} shard(s), {Duplicates} near-duplicate(s) in {Clusters} cluster(s) at threshold {Threshold}, {Parameters}",
            result.Documents, result.Shards.Count, result.Duplicates, result.Clusters.Count, result.Threshold, input.Parameters.Describe());

        var blobId = await BlobService.UploadBytesAsync(CorpusDedupWriter.Write(result, input.Parameters), BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>The completed shards' signature blobs from a corpus report.</summary>
    /// <param name="report">The report bytes.</param>
    /// <returns>Shard index and blob id, in report order.</returns>
    /// <exception cref="InvalidOperationException">Not a corpus report, or a run the merge refuses (see <see cref="CorpusDedupInput"/>).</exception>
    public static List<(int Index, Guid SignaturesBlobId)> ReadSignatureBlobs(byte[] report)
    {
        var input = CorpusDedupInput.Read(report);
        if (input.Refusal != null)
            throw new InvalidOperationException(input.Refusal);

        return input.Shards.Select(shard => (shard.Index, shard.SignaturesBlobId)).ToList();
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
