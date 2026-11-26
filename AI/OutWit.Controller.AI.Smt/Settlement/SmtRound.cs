using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// One fan-out of a run as the host took it in: the packages of problems it gave out, the result
/// packages it took (each under a ticket of this round, holding exactly its package's rows), the
/// ones it refused and why, the packages nodes said they could not work on and why, and the
/// copies of every problem that came back.
/// </summary>
internal sealed class SmtRound
{
    #region Constants

    /// <summary>The first fan-out of a run.</summary>
    public const string MAIN = "main";

    /// <summary>The fan-out of problems without an answer, and of problems of a package asked for twice that came back short of a copy, sent out once more.</summary>
    public const string REPAIR = "repair";

    /// <summary>The fan-out of problems whose copies disagreed or whose model the evaluator called failed, in three copies.</summary>
    public const string DISPUTE = "dispute";

    /// <summary>Every fan-out of a run, in the order a run makes them.</summary>
    public static readonly IReadOnlyList<string> NAMES = [MAIN, REPAIR, DISPUTE];

    #endregion

    #region Fields

    private HashSet<string>? m_answered;

    #endregion

    #region Functions

    /// <summary>The copies of a problem in this round, in an order that follows from their digests.</summary>
    /// <param name="index">The problem.</param>
    /// <returns>The copies; empty when none was taken.</returns>
    public IReadOnlyList<SmtCopy> Of(int index)
    {
        return Copies.TryGetValue(index, out var copies) ? copies : [];
    }

    /// <summary>The copies of a problem in this round that are answers: every one but a fault of its node and a node's refusal.</summary>
    /// <param name="index">The problem.</param>
    /// <returns>The answers.</returns>
    public List<SmtCopy> Answers(int index)
    {
        return Of(index).Where(copy => copy.IsAnswer).ToList();
    }

    /// <summary>What became of a package of problems this round gave out.</summary>
    /// <param name="ticket">The package's ticket.</param>
    /// <returns>Empty when an answer to it was taken; else the node's fault code, the reason a result was refused, or <see cref="SmtRunWords.NO_ANSWER"/>.</returns>
    public string FateOf(string ticket)
    {
        // Asked once the round is taken in: the tickets answered are gathered at the first call.
        m_answered ??= Accepted.Select(package => package.Unit.Ticket).ToHashSet(StringComparer.Ordinal);
        if (m_answered.Contains(ticket))
            return "";

        return Fates.TryGetValue(ticket, out var fate) ? fate : SmtRunWords.NO_ANSWER;
    }

    #endregion

    #region Properties

    /// <summary>The round's name: main, repair or dispute.</summary>
    public required string Name { get; init; }

    /// <summary>The packages of problems the round gave out.</summary>
    public required IReadOnlyList<SmtTaskPackageData> Issued { get; init; }

    /// <summary>The result packages taken, in an order that follows from their content.</summary>
    public List<SmtAcceptedPackage> Accepted { get; } = [];

    /// <summary>The result packages refused, by reason.</summary>
    public SortedDictionary<string, int> Rejected { get; } = new(StringComparer.Ordinal);

    /// <summary>The packages a node said it could not work on, by the node's fault code.</summary>
    public SortedDictionary<string, int> Faults { get; } = new(StringComparer.Ordinal);

    /// <summary>For a ticket no answer was taken under, the last thing that came back for it: a fault code or a reason of refusal.</summary>
    public Dictionary<string, string> Fates { get; } = new(StringComparer.Ordinal);

    /// <summary>The copies taken, by problem.</summary>
    public Dictionary<int, List<SmtCopy>> Copies { get; } = new();

    /// <summary>The package of every problem the round's taken results hold.</summary>
    public Dictionary<int, SmtTaskPackageData> UnitOf { get; } = new();

    /// <summary>
    /// The tickets under which more results were taken than copies were asked for: the surplus
    /// cannot be told from one machine answering twice, so nothing under such a ticket is taken as
    /// agreement.
    /// </summary>
    public HashSet<string> Surplus { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many result references came back for each package of the round, taken or refused: a
    /// bound result for the package whose task it was returned for, whatever it names, an unbound
    /// one under the ticket it names.
    /// </summary>
    public Dictionary<string, int> Returned { get; } = new(StringComparer.Ordinal);

    /// <summary>The result references that came back bound: the platform told the host which package's task each was returned for.</summary>
    public int BoundResults { get; set; }

    /// <summary>The result references that came back unbound: the platform told the host nothing of them.</summary>
    public int UnboundResults { get; set; }

    /// <summary>The result packages refused, every reason together.</summary>
    public int RejectedTotal => Rejected.Values.Sum();

    /// <summary>The packages of problems no taken result answers.</summary>
    public IEnumerable<SmtTaskPackageData> Unanswered
    {
        get
        {
            var answered = Accepted.Select(package => package.Unit.Ticket).ToHashSet(StringComparer.Ordinal);
            return Issued.Where(unit => !answered.Contains(unit.Ticket));
        }
    }

    #endregion
}
