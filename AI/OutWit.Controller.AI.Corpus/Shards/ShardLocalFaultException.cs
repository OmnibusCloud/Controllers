namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A file of the node's own could not be created, written, read or removed: a full disk, a file
/// held by another program, a folder without access. A fault of the node, never of the shard: the
/// shard goes back unanswered and another machine makes its product. The message is the node's
/// own (its log); only the fault's code leaves the node.
/// </summary>
public sealed class ShardLocalFaultException : Exception
{
    #region Constructors

    /// <summary>Creates the exception.</summary>
    /// <param name="code">The fault's code (<see cref="Trust.CorpusResultCodes"/>).</param>
    /// <param name="innerException">What the file system said.</param>
    public ShardLocalFaultException(string code, Exception? innerException = null)
        : base($"A fault of this node: {code}.", innerException)
    {
        Code = code;
    }

    #endregion

    #region Functions

    /// <summary>Whether an exception is one the file system of the node throws.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>True for I/O, access and security failures.</returns>
    public static bool IsLocal(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException;
    }

    #endregion

    #region Properties

    /// <summary>The fault's code.</summary>
    public string Code { get; }

    #endregion
}
