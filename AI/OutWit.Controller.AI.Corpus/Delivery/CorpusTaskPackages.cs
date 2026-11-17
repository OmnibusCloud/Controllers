using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Delivery;

/// <summary>
/// Writes the tasks of a round into task packages on the server: the tasks are taken one at a
/// time, a package is written and uploaded as a blob as soon as it is full, and what is kept is
/// its reference. The tasks of a job of any length are never in memory together, and what the
/// script holds for the round is at most <see cref="CorpusPackagePlanner.MAX_PACKAGES"/> references.
/// </summary>
public static class CorpusTaskPackages
{
    #region Functions

    /// <summary>Writes a round's tasks into packages and uploads them.</summary>
    /// <param name="blobs">Receives the packages.</param>
    /// <param name="tasks">The round's tasks in ascending shard order, each complete; every task of the round shares its pipeline, origins, cap and controller version.</param>
    /// <param name="count">How many tasks there are: it decides how many shards a package holds.</param>
    /// <returns>The packages' references, in shard order; each under a ticket of its own.</returns>
    public static async Task<List<CorpusTaskPackageData>> WriteAsync(IWitBlobService blobs, IEnumerable<CorpusTaskData> tasks, int count)
    {
        var perPackage = CorpusPackagePlanner.ShardsPerPackage(count);
        var packages = new List<CorpusTaskPackageData>(CorpusPackagePlanner.Packages(count));
        var chunk = new List<CorpusTaskData>(perPackage);

        foreach (var task in tasks)
        {
            chunk.Add(task);
            if (chunk.Count < perPackage)
                continue;

            packages.Add(await UploadAsync(blobs, chunk));
            chunk.Clear();
        }

        if (chunk.Count > 0)
            packages.Add(await UploadAsync(blobs, chunk));

        return packages;
    }

    /// <summary>The reference of a package of the given tasks around a blob that is already written: what a node needs before it opens the blob.</summary>
    /// <param name="tasks">The package's tasks.</param>
    /// <param name="blobId">The blob.</param>
    /// <param name="sha256">Lower-case hex SHA-256 of the blob's bytes.</param>
    /// <param name="sizeBytes">The blob's size.</param>
    /// <returns>The reference, under a new ticket.</returns>
    public static CorpusTaskPackageData Describe(IReadOnlyList<CorpusTaskData> tasks, Guid blobId, string sha256, long sizeBytes)
    {
        long bytes = 0, largest = 0;
        var pdf = false;
        var unsized = 0;

        foreach (var task in tasks)
        {
            bytes += Math.Max(0, task.Source?.SizeBytes ?? 0);
            if ((task.Source?.SizeBytes ?? 0) <= 0)
                unsized++;

            largest = Math.Max(largest, ShardBatchGuard.ShardBytes(task));
            pdf |= task.Source?.Format == CorpusSourceFormat.Warc && task.Pipeline?.ExtractPdf == true;
        }

        return new CorpusTaskPackageData
        {
            TasksBlobId = blobId,
            Sha256 = sha256,
            SizeBytes = sizeBytes,
            ShardCount = tasks.Count,
            ShardBytes = bytes,
            UnsizedShards = unsized,
            LargestShardBytes = largest,
            NeedsPdfSandbox = pdf,
            Ticket = CorpusTickets.New(),
            RequiredControllerVersion = tasks.Count > 0 ? tasks[0].RequiredControllerVersion : ""
        };
    }

    #endregion

    #region Tools

    private static async Task<CorpusTaskPackageData> UploadAsync(IWitBlobService blobs, List<CorpusTaskData> tasks)
    {
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            CorpusPackageCodec.WriteTasks(buffer, tasks[0], tasks);
            bytes = buffer.ToArray();
        }

        var blobId = await blobs.UploadBytesAsync(bytes, CorpusPackageCodec.TASKS_FILE_NAME);

        return Describe(tasks, blobId, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length);
    }

    #endregion
}
