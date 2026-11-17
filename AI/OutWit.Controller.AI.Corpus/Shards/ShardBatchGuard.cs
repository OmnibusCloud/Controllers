using System.Collections.Concurrent;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// What a node can know before it does any work, checked at the start of every task of a batch,
/// before a byte is fetched: the controller version the job runs on, the PDF sandbox when the task
/// is a WARC shard with the PDF stage on (the binding, the natives guard, the module and its pin:
/// everything <see cref="PdfSandboxHost"/> checks when it makes the sandbox), and room on the disk
/// for the shard. A node that fails one refuses its batch (<see cref="ShardBatchRefusedException"/>)
/// and the platform moves the work, so nothing a node has already done is lost to a fault it could
/// have named at the start. The client starts every task of a batch together; a refusal is
/// remembered for the job for a while, so the batch's other tasks, which may still be waiting for
/// their turn to fetch, stop before they do.
/// </summary>
public static class ShardBatchGuard
{
    #region Constants

    /// <summary>How long a job's refusal on this node is remembered: the batch's other tasks stop, a later round is judged afresh.</summary>
    public static readonly TimeSpan MEMORY = TimeSpan.FromMinutes(2);

    private const int MAX_REMEMBERED = 256;

    #endregion

    #region Fields

    private static readonly ConcurrentDictionary<Guid, (ShardBatchRefusedException Refusal, DateTime Until)> m_refused = new();

    #endregion

    #region Functions

    /// <summary>Checks a task on this node and throws when the node must refuse its batch.</summary>
    /// <param name="jobId">The job the task belongs to.</param>
    /// <param name="task">The task.</param>
    /// <param name="workDirectory">The folder the shard would be fetched into.</param>
    /// <exception cref="ShardBatchRefusedException">The node refuses the batch.</exception>
    public static void ThrowIfRefused(Guid jobId, CorpusTaskData task, string workDirectory)
    {
        ThrowIfJobRefused(jobId);

        var refusal = Check(task, ControllerBuildInfo.VERSION, () => PdfSandboxHost.Problem, () => ShardDisk.HasRoom(workDirectory, ShardBytes(task)));
        if (refusal == null)
            return;

        if (m_refused.Count >= MAX_REMEMBERED)
            m_refused.Clear();

        m_refused[jobId] = (refusal, DateTime.UtcNow + MEMORY);
        throw refusal;
    }

    /// <summary>Throws when this node refused a batch of the job a moment ago: the batch is lost, and its other tasks stop here.</summary>
    /// <param name="jobId">The job.</param>
    /// <exception cref="ShardBatchRefusedException">The node refused a task of the job within <see cref="MEMORY"/>.</exception>
    public static void ThrowIfJobRefused(Guid jobId)
    {
        if (!m_refused.TryGetValue(jobId, out var remembered))
            return;

        if (remembered.Until > DateTime.UtcNow)
            throw new ShardBatchRefusedException(remembered.Refusal.Code, remembered.Refusal.Message);

        m_refused.TryRemove(jobId, out _);
    }

    /// <summary>Why a node refuses a task's batch, or null when it takes the task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="ownVersion">The node's controller version.</param>
    /// <param name="pdfProblem">Says why the node has no PDF sandbox (null when it has one); asked only for a WARC shard with the PDF stage on.</param>
    /// <param name="hasRoom">Says whether the disk has room for the shard.</param>
    /// <returns>The refusal, or null.</returns>
    public static ShardBatchRefusedException? Refusal(CorpusTaskData task, string ownVersion, Func<string?> pdfProblem, Func<bool> hasRoom)
    {
        if (!string.Equals(task.RequiredControllerVersion, ownVersion, StringComparison.Ordinal))
        {
            // The required version is the task's own text: it is repeated only when it has the shape of a version.
            var required = CorpusText.IsName(task.RequiredControllerVersion, CorpusResultCheck.MAX_LABEL_CHARS, ".-+") ? task.RequiredControllerVersion : "another version";
            return new ShardBatchRefusedException(ShardBatchRefusedException.VERSION, $"This node runs Corpus {ownVersion} and the job requires {required}: one job runs on one controller version.");
        }

        if (task.Source?.Format == CorpusSourceFormat.Warc && task.Pipeline?.ExtractPdf == true && pdfProblem() != null)
            return new ShardBatchRefusedException(ShardBatchRefusedException.PDF_SANDBOX, "This node cannot run the PDF stage and the batch holds a WARC shard that needs it: another node takes the batch.");

        if (!hasRoom())
            return new ShardBatchRefusedException(ShardBatchRefusedException.DISK_SPACE, "This node's disk has no room for a shard of the batch: another node takes the batch.");

        return null;
    }

    /// <summary>
    /// <see cref="Refusal"/>, and a refusal too when the node fails while it asks itself (a sandbox
    /// whose library does not load in a way nobody foresaw): the failure's own text would reach
    /// the lab as the job's message, so it stays here and the batch is refused in fixed words.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="ownVersion">This node's controller version.</param>
    /// <param name="pdfProblem">Says why this node has no PDF sandbox, or null when it has one.</param>
    /// <param name="hasRoom">Says whether the disk has room for the shard.</param>
    /// <returns>The refusal, or null.</returns>
    internal static ShardBatchRefusedException? Check(CorpusTaskData task, string ownVersion, Func<string?> pdfProblem, Func<bool> hasRoom)
    {
        try
        {
            return Refusal(task, ownVersion, pdfProblem, hasRoom);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ShardBatchRefusedException(ShardBatchRefusedException.INTERNAL, "This node failed before it could start the batch: another node takes the batch.");
        }
    }

    /// <summary>
    /// Makes the folder a task works in. A node that cannot refuses its batch in fixed words: the
    /// failure's own text names the node's folders, and what leaves the activity is shown to the lab.
    /// </summary>
    /// <param name="storage">The host's temp storage.</param>
    /// <param name="label">The scope's label.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="ShardBatchRefusedException">The folder could not be made.</exception>
    public static string CreateScope(OutWit.Engine.Interfaces.IWitTempStorage storage, string label)
    {
        try
        {
            return storage.CreateScope(label);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ShardBatchRefusedException(ShardBatchRefusedException.DISK, "This node cannot make its work folder: another node takes the batch.");
        }
    }

    /// <summary>Forgets every remembered refusal (tests).</summary>
    public static void Clear()
    {
        m_refused.Clear();
    }

    /// <summary>The shard's size as the task states it, held to the node's own cap; 0 when the task does not say.</summary>
    internal static long ShardBytes(CorpusTaskData task)
    {
        var size = task.Source?.SizeBytes ?? 0;
        return size <= 0 ? 0 : Math.Min(size, Manifests.CorpusTaskPlanner.NodeShardCap(task.MaxShardBytes));
    }

    #endregion
}
