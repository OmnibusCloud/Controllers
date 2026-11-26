namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// One package's turn in the node's budget of sessions (<see cref="SmtSessionBudget"/>), as the
/// package holds it from the moment it is let in. With it the package reads the degree its session
/// size gets, hands its problems out (<see cref="RunAsync"/>), and gives its place up by disposing
/// it when its solve stage is over, whatever the way it ended: a package that was let in and
/// refused gives its place to the next one so.
/// <para>
/// The turn is all a package has of the budget: three things to ask, each of which the budget
/// does under its own lock. What the budget counts of the package (its problems handed out and
/// running, its failure) is the budget's own (<see cref="SmtSessionBudgetPlace"/>) and is not
/// to be had through the turn.
/// </para>
/// </summary>
internal sealed class SmtSessionBudgetTurn : IDisposable
{
    #region Fields

    private readonly SmtSessionBudget m_budget;

    private readonly SmtSessionBudgetPlace m_place;

    #endregion

    #region Constructors

    /// <summary>Makes the turn of a place in a budget.</summary>
    /// <param name="budget">The budget.</param>
    /// <param name="place">The package's place in it.</param>
    public SmtSessionBudgetTurn(SmtSessionBudget budget, SmtSessionBudgetPlace place)
    {
        m_budget = budget;
        m_place = place;
    }

    #endregion

    #region Functions

    /// <summary>The sessions of a size the package gets at once on the budget's reading of the machine (<see cref="SmtSandboxDegree.Compute"/>).</summary>
    /// <param name="sessionBytes">The memory one session of the package may take.</param>
    /// <returns>The degree, and what decided it; 0 when not one session fits.</returns>
    public SmtDegree Degree(long sessionBytes)
    {
        return m_budget.Degree(m_place, sessionBytes);
    }

    /// <summary>
    /// Hands the package's problems out to the budget's sessions: <paramref name="body"/> runs once
    /// for every index in <c>[0, count)</c>, in order of the start, at most as many at once as the
    /// budget allows beside the problems of the other packages.
    /// </summary>
    /// <param name="count">The problems.</param>
    /// <param name="sessionBytes">The memory one session of the package may take.</param>
    /// <param name="body">The work of one problem, by its index in the package; runs on a session's thread, concurrently with itself.</param>
    /// <param name="cancellationToken">Once cancelled, no further problem starts; those running finish, then the task ends cancelled.</param>
    /// <returns>A task that ends when every problem handed out has ended: with the first failure of one, cancelled, or done.</returns>
    /// <exception cref="InvalidOperationException">The turn is not one given and not yet running.</exception>
    public Task RunAsync(int count, long sessionBytes, Action<int> body, CancellationToken cancellationToken)
    {
        return m_budget.Run(m_place, count, sessionBytes, body, cancellationToken);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Gives the package's place up: a turn given and not running leaves at once; a running one
    /// hands out nothing more and leaves when its sessions have ended.
    /// </summary>
    public void Dispose()
    {
        m_budget.Leave(m_place);
    }

    #endregion
}
