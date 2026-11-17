namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// One round as the ledger reads it: the task packages the server made, in ascending shard order,
/// each with what came back under its tickets. A source is read one package at a time and may be
/// read again from its start, so a run of any length is judged without its tasks or its results
/// ever being in memory together. What a source knows before a package is opened — how many
/// shards the round has, which values belong to no package of it, how many answers each package
/// got — it says at once.
/// </summary>
public abstract class CorpusRoundSource
{
    #region Constructors

    /// <summary>Creates a source for a round.</summary>
    /// <param name="kind">Which round.</param>
    protected CorpusRoundSource(CorpusRoundKind kind)
    {
        Kind = kind;
    }

    #endregion

    #region Functions

    /// <summary>The answers a round asks of a package: itself and one copy in the first round, one in the repair round, three in the dispute round.</summary>
    /// <param name="kind">The round.</param>
    /// <returns>The count.</returns>
    public static int AskedOf(CorpusRoundKind kind)
    {
        return kind switch
        {
            CorpusRoundKind.First => CorpusLedger.FIRST_ROUND_COPIES,
            CorpusRoundKind.Repair => CorpusLedger.REPAIR_COPIES,
            _ => CorpusLedger.DISPUTE_COPIES
        };
    }

    /// <summary>
    /// The answers of a package that are opened: one more than the round asks for. That one more
    /// is enough for every ruling to see that more came back than were asked; an answer beyond it
    /// is dropped unopened (<see cref="CorpusLedger.REJECTED_SURPLUS"/>), so a machine that
    /// answers one package in every slot it holds costs the server a bounded reading.
    /// </summary>
    /// <param name="kind">The round.</param>
    /// <returns>The count.</returns>
    public static int OpenedOf(CorpusRoundKind kind)
    {
        return AskedOf(kind) + 1;
    }

    /// <summary>Reads the round's packages from the first, in ascending shard order, one at a time.</summary>
    /// <returns>The packages; each is built when asked for and not kept.</returns>
    public abstract IEnumerable<CorpusRoundPackage> Read();

    #endregion

    #region Properties

    /// <summary>Which round.</summary>
    public CorpusRoundKind Kind { get; }

    /// <summary>The answers the round asks of each package.</summary>
    public int Asked => AskedOf(Kind);

    /// <summary>The shards of the round: its tasks.</summary>
    public abstract int Shards { get; }

    /// <summary>The values that belong to no package of the round, by reason: nothing of them is opened.</summary>
    public abstract IReadOnlyDictionary<string, int> Unbound { get; }

    /// <summary>What the round is short of: over its packages, the answers each was asked for and did not get.</summary>
    public abstract int Missing { get; }

    #endregion
}
