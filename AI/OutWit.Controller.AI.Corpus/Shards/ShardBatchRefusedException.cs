namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The node refuses its batch before doing anything: it runs another controller version than the
/// job, it cannot run the PDF stage a shard of the batch needs, or its disk has no room for a
/// shard. Thrown from <c>Corpus.ProcessPackage</c> before a byte is fetched, so the batch fails with
/// nothing lost and the platform gives its tasks to another node. The same goes for a node that
/// cannot make its work folder, or that fails while it checks itself: whatever a node throws out
/// of the activity is shown to the lab, so nothing but this exception leaves it. The message
/// leaves the node (the platform shows the last one when no node can take the work): fixed words,
/// versions and a code, never a path, a name of the machine or an exception's own text.
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

    /// <summary>The node cannot make the folder a task works in.</summary>
    public const string DISK = "disk";

    /// <summary>The node failed while it checked itself, before any work.</summary>
    public const string INTERNAL = "internal";

    /// <summary>Every reason a node refuses a batch for, before it does any of its work.</summary>
    public static readonly IReadOnlyList<string> CODES = [VERSION, PDF_SANDBOX, DISK_SPACE, DISK, INTERNAL];

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
