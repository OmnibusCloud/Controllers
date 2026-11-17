using System.Net;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// A fetch that did not bring the shard, after every retry, and whose trouble it was: the
/// origin's, which answered with a status that is not a success (a statement about the shard:
/// the same for any node that asks then), or nobody's the node can name — no connection, no
/// answer, a body that kept breaking — which may be the node's own link and is therefore a fault
/// of the node, never a verdict on the shard.
/// </summary>
public sealed class ShardFetchException : Exception
{
    #region Constructors

    /// <summary>The origin answered with a status that is not a success.</summary>
    /// <param name="status">The status of the last answer.</param>
    public ShardFetchException(HttpStatusCode status)
        : base($"The origin answered {(int)status}.")
    {
        Status = status;
    }

    /// <summary>The origin did not answer, or its answer kept breaking.</summary>
    /// <param name="attempts">How many times it was tried.</param>
    /// <param name="innerException">The last failure.</param>
    public ShardFetchException(int attempts, Exception? innerException)
        : base($"The origin gave no complete answer in {attempts} attempt(s).", innerException)
    {
    }

    #endregion

    #region Properties

    /// <summary>The HTTP status the origin refused with; null when it gave no complete answer at all.</summary>
    public HttpStatusCode? Status { get; }

    #endregion
}
