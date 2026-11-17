namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A request was not made, or not continued, because it would have left what this machine
/// fetches from: a host outside the task's list or the machine's policy, a name that resolves
/// to a non-public address, a redirect to such a place. Final: a retry cannot change it.
/// </summary>
public sealed class ShardOriginRefusedException : Exception
{
    #region Constructors

    /// <summary>Creates the exception.</summary>
    /// <param name="code">The refusal's code (<see cref="Trust.CorpusResultCodes"/>): what a result says of it.</param>
    /// <param name="message">What was refused and why: the node's own log reads it, nothing else does.</param>
    public ShardOriginRefusedException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    #endregion

    #region Functions

    /// <summary>Finds a refusal in an exception or anything it wraps (the HTTP stack wraps what a connection attempt throws).</summary>
    /// <param name="exception">The exception to look into.</param>
    /// <returns>The refusal, or null.</returns>
    public static ShardOriginRefusedException? Find(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is ShardOriginRefusedException refusal)
                return refusal;
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>The refusal's code.</summary>
    public string Code { get; }

    #endregion
}
