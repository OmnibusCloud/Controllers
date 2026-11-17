using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// One shard in the ledger: its task as the server made it, what came back under its tickets in
/// each round, and how it ended. Everything here that names the shard (its index, its URL, its
/// pipeline) is the server's own; a node contributes only what stands in an accepted result.
/// </summary>
public sealed class CorpusLedgerShard
{
    #region Constructors

    /// <summary>Creates the shard's record.</summary>
    /// <param name="task">The first round's task.</param>
    public CorpusLedgerShard(CorpusTaskData task)
    {
        Task = task;
    }

    #endregion

    #region Properties

    /// <summary>The task the server made for the shard in the first round.</summary>
    public CorpusTaskData Task { get; }

    /// <summary>The shard's index in the manifest and the report.</summary>
    public int Index => Task.TaskIndex;

    /// <summary>The shard's URL, from the task.</summary>
    public string Url => Task.Source?.Url ?? "";

    /// <summary>What came back under the first round's ticket, in the platform's order.</summary>
    public List<CorpusLedgerEntry> First { get; } = [];

    /// <summary>What came back under the repair round's ticket.</summary>
    public List<CorpusLedgerEntry> Repair { get; } = [];

    /// <summary>What came back under the dispute round's ticket.</summary>
    public List<CorpusLedgerEntry> Dispute { get; } = [];

    /// <summary>Whether the first round left the shard without an accepted result and without a disagreement: it is to be sent out once more.</summary>
    public bool NeedsRepair { get; internal set; }

    /// <summary>Whether the shard's copies disagreed (or more came back than were sent out): it is to be run on three machines.</summary>
    public bool NeedsDispute { get; internal set; }

    /// <summary>Whether the repair round held a task for the shard.</summary>
    public bool SentToRepair { get; internal set; }

    /// <summary>Whether the dispute round held a task for the shard.</summary>
    public bool SentToDispute { get; internal set; }

    /// <summary>How the shard ended.</summary>
    public CorpusShardOutcome Outcome { get; internal set; } = CorpusShardOutcome.NoResult;

    /// <summary>The accepted result; null unless <see cref="Outcome"/> is <see cref="CorpusShardOutcome.Accepted"/>.</summary>
    public CorpusResultData? Accepted { get; internal set; }

    /// <summary>How many results the accepted one rests on: 1 is one machine's word, 2 a pair that agreed, 2 or 3 a majority of the dispute round; 0 when nothing was accepted.</summary>
    public int Copies { get; internal set; }

    /// <summary>Why there is no result: one of <see cref="CorpusLedger.NO_RESULT_REASONS"/>; "" otherwise.</summary>
    public string Reason { get; internal set; } = "";

    /// <summary>The fault codes the nodes returned for the shard over every round, each once, in ordinal order.</summary>
    public IReadOnlyList<string> Faults { get; internal set; } = [];

    #endregion
}
