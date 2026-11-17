using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// One fan-out of a run as the server accepted it: the task packages it gave out, the result
/// packages it took (each under a ticket of this round, holding exactly its package's tasks),
/// how many it refused and why, and the executions of every task that came back.
/// </summary>
internal sealed class VerifyRound
{
    #region Constants

    /// <summary>The first fan-out of a run.</summary>
    public const string MAIN = "main";

    /// <summary>The fan-out of tasks that had no verdict, sent out once more.</summary>
    public const string REPAIR = "repair";

    /// <summary>The fan-out of tasks whose copies disagreed, in three copies.</summary>
    public const string DISPUTE = "dispute";

    #endregion

    #region Functions

    /// <summary>The executions of a task in this round, in an order that follows from their content.</summary>
    /// <param name="index">The task.</param>
    /// <returns>The executions; empty when none was accepted.</returns>
    public IReadOnlyList<VerifyExecution> Of(int index)
    {
        return Executions.TryGetValue(index, out var executions) ? executions : [];
    }

    /// <summary>The executions of a task in this round that are verdicts.</summary>
    /// <param name="index">The task.</param>
    /// <returns>The executions with a verdict.</returns>
    public List<VerifyExecution> Verdicts(int index)
    {
        return Of(index).Where(execution => execution.HasVerdict).ToList();
    }

    /// <summary>How many result packages were accepted under a ticket.</summary>
    /// <param name="ticket">The ticket of a task package of this round.</param>
    /// <returns>The copies accepted.</returns>
    public int Copies(string ticket)
    {
        return Accepted.Count(package => package.Unit.Ticket == ticket);
    }

    #endregion

    #region Properties

    /// <summary>The round's name: main, repair or dispute.</summary>
    public required string Name { get; init; }

    /// <summary>The task packages the round gave out.</summary>
    public required IReadOnlyList<VerifyTaskPackageData> Issued { get; init; }

    /// <summary>The result packages accepted, in an order that follows from their content.</summary>
    public List<VerifyAcceptedPackage> Accepted { get; } = [];

    /// <summary>The result packages refused, by reason code.</summary>
    public SortedDictionary<string, int> Rejected { get; } = new(StringComparer.Ordinal);

    /// <summary>The executions accepted, by task.</summary>
    public Dictionary<int, List<VerifyExecution>> Executions { get; } = new();

    /// <summary>The task of every index the round's accepted packages hold, with the task package it came in.</summary>
    public Dictionary<int, VerifyTaskPackageData> UnitOf { get; } = new();

    /// <summary>
    /// The tickets under which more results came back than copies were asked for. The surplus
    /// cannot be told from one machine answering twice, so nothing under such a ticket is taken
    /// as agreement.
    /// </summary>
    public HashSet<string> Surplus { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How many result references came back under each ticket of the round, taken or refused.
    /// A node returns one reference for each package it was given, whatever it puts into it.
    /// </summary>
    public Dictionary<string, int> Returned { get; } = new(StringComparer.Ordinal);

    /// <summary>The result packages refused, all reasons together.</summary>
    public int RejectedTotal => Rejected.Values.Sum();

    /// <summary>The task packages no accepted result answers.</summary>
    public IEnumerable<VerifyTaskPackageData> Unanswered
    {
        get
        {
            var answered = Accepted.Select(package => package.Unit.Ticket).ToHashSet(StringComparer.Ordinal);
            return Issued.Where(unit => !answered.Contains(unit.Ticket));
        }
    }

    #endregion
}
