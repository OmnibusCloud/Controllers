namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The node refuses its batch before doing anything: it runs another controller version than the
/// job, it cannot run the PDF stage a shard of the batch needs, or its disk has no room for a
/// shard. Thrown from <c>Corpus.ProcessShard</c> before a byte is fetched, so the batch fails with
/// nothing lost and the platform gives its tasks to another node. The message leaves the node
/// (the platform shows the last one when no node can take the work): fixed words, versions and a
/// code, never a path or a name of the machine.
/// </summary>
public sealed class ShardBatchRefusedException : Exception
{
    #region Constants

    /// <summary>The node runs another controller version than the job requires.</summary>
    public const string VERSION = "version";

    /// <summary>The batch holds a WARC shard with the PDF stage on and this node has no PDF sandbox.</summary>
    public const string PDF_SANDBOX = "pdf_sandbox";

    /// <summary>The node's disk has less room than a shard of the batch needs.</summary>
    public const string DISK_SPACE = "disk_space";

    #endregion

    #region Constructors

    /// <summary>Creates the refusal.</summary>
    /// <param name="code">Why: <see cref="VERSION"/>, <see cref="PDF_SANDBOX"/> or <see cref="DISK_SPACE"/>.</param>
    /// <param name="message">The fixed words that leave the node.</param>
    public ShardBatchRefusedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    #endregion

    #region Properties

    /// <summary>Why the batch is refused.</summary>
    public string Code { get; }

    #endregion
}
