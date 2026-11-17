using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
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

        // The report is a lab's input: its size is asked before it is read.
        var reportPath = await BlobService.GetLocalPathAsync(reportBlobId);
        if (new FileInfo(reportPath).Length > CorpusDedupInput.MAX_REPORT_BYTES)
            throw new InvalidOperationException($"Corpus.Dedup: the report is larger than {CorpusDedupInput.MAX_REPORT_BYTES} bytes.");

        // A run without signatures is refused by name, and one with more documents than a merge
        // holds: here, from the report's own counts, before a blob is opened. The report is read
        // as a stream, a shard at a time: it grows with its run and is never held.
        var input = await Task.Run(() =>
        {
            using var file = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return CorpusDedupInput.Read(file);
        }, cancellationToken);
        if (input.Refusal != null)
            throw new InvalidOperationException($"Corpus.Dedup: {input.Refusal}");

        // Every blob once, each held to the hash and the document count the report states; one
        // that is not what the report says is left out and named.
        var (shards, excluded) = await CorpusDedupLoader.LoadAsync(input, blobId => BlobService.GetLocalPathAsync(blobId), cancellationToken);
        foreach (var exclusion in excluded)
            Logger.LogWarning("Corpus.Dedup: shard {Index} is left out of the merge ({Reason}).", exclusion.Index, exclusion.Reason);

        var result = await Task.Run(() => new CorpusDeduplicator(options?.DedupThreshold ?? 0, input.Parameters.Bands).Run(shards, cancellationToken), cancellationToken) with { Excluded = excluded };
        Logger.LogInformation("Corpus.Dedup: {Documents} document(s) in {Shards} shard(s), {Duplicates} near-duplicate(s) in {Clusters} cluster(s) at threshold {Threshold}, {Parameters}; {Excluded} shard(s) left out",
            result.Documents, result.Shards.Count, result.Duplicates, result.Clusters.Count, result.Threshold, input.Parameters.Describe(), excluded.Count);

        var blobId = await BlobService.UploadBytesAsync(CorpusDedupWriter.Write(result, input.Parameters), BLOB_NAME);

        if (!pool.TrySetValue(activity.ReturnReference, blobId))
            throw new InvalidOperationException($"Failed to set return value '{activity.ReturnReference}'.");
    }

    /// <summary>The completed shards' signature blobs from a corpus report.</summary>
    /// <param name="report">The report bytes.</param>
    /// <returns>Shard index and blob id, in index order.</returns>
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
