using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// A round held in memory (<see cref="CorpusRound"/>): each shard of it is a package of its own
/// and each value an answer to it. The form the ledger's rulings are stated and tested in; a job
/// run through the platform reads its rounds from blobs (<see cref="CorpusRoundSourceBlobs"/>).
/// </summary>
public sealed class CorpusRoundSourceMemory : CorpusRoundSource
{
    #region Constants

    private static readonly IReadOnlyDictionary<string, int> NOTHING = new Dictionary<string, int>();

    #endregion

    #region Fields

    private readonly List<CorpusRoundPackage> m_packages = [];
    private readonly SortedDictionary<string, int> m_unbound = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>Binds the values of a round to its tasks by their tickets.</summary>
    /// <param name="round">The round: the tasks the server made and whatever came back.</param>
    public CorpusRoundSourceMemory(CorpusRound round)
        : base(round.Kind)
    {
        // The shards of the round in index order, each with every task made for it. A task
        // without a ticket of the right shape, or under a ticket another task of the round
        // has, holds no ticket: nothing can be bound to it. In the first round it still stands
        // for its shard; in a later round it was never sent.
        var groups = new SortedDictionary<int, List<CorpusRoundSlot>>();
        var tickets = new Dictionary<string, (int Index, CorpusRoundSlot Slot)>(StringComparer.Ordinal);

        foreach (var task in round.Tasks)
        {
            var ticketed = CorpusTickets.IsWellFormed(task.Ticket) && !tickets.ContainsKey(task.Ticket);
            if (!ticketed && Kind != CorpusRoundKind.First)
                continue;

            var slot = new CorpusRoundSlot(task);
            if (!groups.TryGetValue(task.TaskIndex, out var slots))
                groups[task.TaskIndex] = slots = [];

            slots.Add(slot);
            if (ticketed)
                tickets[task.Ticket] = (task.TaskIndex, slot);
        }

        var returned = new Dictionary<int, int>();
        var dropped = new Dictionary<int, Dictionary<string, int>>();
        long values = 0;

        foreach (var value in round.Results)
        {
            // A null is no value: the slot it stands for brought nothing.
            if (value != null)
                values++;

            if (value is not CorpusResultData result)
            {
                Count(m_unbound, CorpusLedger.REJECTED_UNREADABLE);
                continue;
            }

            if (!CorpusTickets.IsWellFormed(result.Ticket) || !tickets.TryGetValue(result.Ticket, out var bound))
            {
                Count(m_unbound, CorpusLedger.REJECTED_UNKNOWN_TICKET);
                continue;
            }

            var answers = returned.GetValueOrDefault(bound.Index) + 1;
            returned[bound.Index] = answers;

            if (answers > OpenedOf(Kind))
            {
                if (!dropped.TryGetValue(bound.Index, out var reasons))
                    dropped[bound.Index] = reasons = new Dictionary<string, int>(StringComparer.Ordinal);

                Count(reasons, CorpusLedger.REJECTED_SURPLUS);
                continue;
            }

            bound.Slot.Values.Add(CorpusRoundValue.Of(result));
        }

        foreach (var (index, slots) in groups)
        {
            var answers = returned.GetValueOrDefault(index);
            m_packages.Add(new CorpusRoundPackage(slots, answers, dropped.GetValueOrDefault(index) ?? NOTHING));
            Missing += Math.Max(0, Asked - answers);
        }

        Answers = CorpusRoundAnswers.Of(m_packages.Select(package => package.Returned));
        Silent = SilentOf(m_packages.Count, Asked, values);
    }

    #endregion

    #region Functions

    /// <inheritdoc />
    public override IEnumerable<CorpusRoundPackage> Read()
    {
        return m_packages;
    }

    private static void Count(IDictionary<string, int> counts, string reason)
    {
        counts[reason] = counts.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override int Shards => m_packages.Count;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, int> Unbound => m_unbound;

    /// <inheritdoc />
    public override int Missing { get; }

    /// <inheritdoc />
    public override CorpusRoundAnswers Answers { get; }

    /// <inheritdoc />
    public override int? Silent { get; }

    #endregion
}
