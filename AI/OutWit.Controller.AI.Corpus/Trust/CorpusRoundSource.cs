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

    /// <summary>
    /// The most answers a round asks of a package: itself and one copy in the first round (of the
    /// packages the run's sampling picks) and in the repair round (of every package, when the run
    /// asked for sampling at all), three in either dispute round.
    /// </summary>
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

    /// <summary>The most answers the round asks of a package.</summary>
    public int Asked => AskedOf(Kind);

    /// <summary>The shards of the round: its tasks.</summary>
    public abstract int Shards { get; }

    /// <summary>The values that belong to no package of the round, by reason: nothing of them is opened.</summary>
    public abstract IReadOnlyDictionary<string, int> Unbound { get; }

    /// <summary>
    /// What the round is short of: over its packages, the answers each could have got at the most
    /// (<see cref="Asked"/>) and did not. The ledger reads it of the dispute round only, where
    /// every package is asked for in three copies; in the other rounds a package that was not
    /// picked for a second machine counts as one short here (<see cref="Answers"/> says how a
    /// round came back without assuming what was asked of each package).
    /// </summary>
    public abstract int Missing { get; }

    /// <summary>How the round's fan-out came back: its packages by the answers each got, known before any is opened.</summary>
    public abstract CorpusRoundAnswers Answers { get; }

    /// <summary>
    /// The slots of the round that brought nothing at all: its packages times <see cref="Asked"/>,
    /// less every value that came back (bound to a package or not; a null is no value). A slot
    /// whose value came back under no ticket of the round lent nothing to another package, so
    /// only a silent slot can be where one machine's second answer for a shard came from. Null
    /// when more values came back than the round had slots: the platform then kept more than one
    /// value of a task, and nothing is read from the count. The ledger reads it of the dispute
    /// rounds only.
    /// </summary>
    public abstract int? Silent { get; }

    /// <summary>The slots of a round less the values that came back; null when more came back than there were slots.</summary>
    /// <param name="packages">The round's packages.</param>
    /// <param name="asked">The answers it asks of a package.</param>
    /// <param name="values">The values that came back, of any kind but null.</param>
    /// <returns>The silent slots, or null.</returns>
    protected static int? SilentOf(int packages, int asked, long values)
    {
        var slots = (long)packages * asked;
        return values > slots ? null : (int)(slots - values);
    }

    #endregion
}
