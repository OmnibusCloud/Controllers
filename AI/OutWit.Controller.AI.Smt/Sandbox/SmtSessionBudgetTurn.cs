namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>Where a package's turn stands in the budget.</summary>
internal enum SmtSessionBudgetTurnState
{
    /// <summary>The package waits to be let in.</summary>
    Waiting,

    /// <summary>The package is let in: it is checked and opened, or its problems are handed out and running.</summary>
    Open,

    /// <summary>The package has left the budget.</summary>
    Left
}

/// <summary>
/// One package's place in the node's budget of sessions (<see cref="SmtSessionBudget"/>), from the
/// wait for its turn to the end of its last problem. The package asks for it, is let in, reads the
/// degree its session size gets, hands its problems out with <see cref="RunAsync"/> and disposes it
/// when its solve stage is over, whatever the way it ended: a package that was let in and refused
/// gives its place to the next one so.
/// <para>
/// What the budget counts of the package (its problems handed out and running, its failure) is
/// changed under the budget's lock only.
/// </para>
/// </summary>
internal sealed class SmtSessionBudgetTurn : IDisposable
{
    #region Fields

    private readonly SmtSessionBudget m_budget;

    private readonly TaskCompletionSource<SmtSessionBudgetTurn> m_granted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource m_done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Exception? m_failure;

    private bool m_halted;

    #endregion

    #region Constructors

    /// <summary>Makes the place of a package in a budget.</summary>
    /// <param name="budget">The budget.</param>
    public SmtSessionBudgetTurn(SmtSessionBudget budget)
    {
        m_budget = budget;
    }

    #endregion

    #region Functions

    /// <summary>The sessions of a size the package gets at once on the budget's reading of the machine (<see cref="SmtSandboxDegree.Compute"/>).</summary>
    /// <param name="sessionBytes">The memory one session of the package may take.</param>
    /// <returns>The degree, and what decided it; 0 when not one session fits.</returns>
    public SmtDegree Degree(long sessionBytes)
    {
        return m_budget.Degree(this, sessionBytes);
    }

    /// <summary>Hands the package's problems out to the budget's sessions (<see cref="SmtSessionBudget.Run"/>).</summary>
    /// <param name="count">The problems.</param>
    /// <param name="sessionBytes">The memory one session of the package may take.</param>
    /// <param name="body">The work of one problem, by its index in the package.</param>
    /// <param name="cancellationToken">Once cancelled, no further problem starts; those running finish, then the task ends cancelled.</param>
    /// <returns>A task that ends when every problem handed out has ended: with the first failure of one, cancelled, or done.</returns>
    public Task RunAsync(int count, long sessionBytes, Action<int> body, CancellationToken cancellationToken)
    {
        return m_budget.Run(this, count, sessionBytes, body, cancellationToken);
    }

    /// <summary>Sets what the package runs: it is running from now on. Under the budget's lock.</summary>
    /// <param name="count">The problems.</param>
    /// <param name="sessionBytes">The memory a session takes.</param>
    /// <param name="body">The work of one problem.</param>
    /// <param name="cancellationToken">The package's token.</param>
    public void Start(int count, long sessionBytes, Action<int> body, CancellationToken cancellationToken)
    {
        Count = Math.Max(0, count);
        SessionBytes = Math.Max(0, sessionBytes);
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Token = cancellationToken;
    }

    /// <summary>Hands the next problem out: its index. Under the budget's lock.</summary>
    /// <returns>The index.</returns>
    public int Take()
    {
        Running++;
        return Handed++;
    }

    /// <summary>A problem's session has ended, with the exception it threw or null. Under the budget's lock.</summary>
    /// <param name="failure">The exception, or null.</param>
    public void Ended(Exception? failure)
    {
        Running--;
        if (failure != null)
            Fail(failure);
    }

    /// <summary>The package fails: the first failure is kept, and nothing more is handed out. Under the budget's lock.</summary>
    /// <param name="failure">The exception.</param>
    public void Fail(Exception failure)
    {
        m_failure ??= failure;
        m_halted = true;
    }

    /// <summary>Nothing more of the package is handed out (it was cancelled). Under the budget's lock.</summary>
    public void Halt()
    {
        m_halted = true;
    }

    /// <summary>The package is let in.</summary>
    public void Admit()
    {
        m_granted.TrySetResult(this);
    }

    /// <summary>The wait for the turn was given up.</summary>
    /// <param name="cancellationToken">The token that gave it up.</param>
    public void Cancel(CancellationToken cancellationToken)
    {
        m_granted.TrySetCanceled(cancellationToken);
    }

    /// <summary>The wait for the turn ended without one.</summary>
    /// <param name="failure">Why.</param>
    public void Refuse(Exception failure)
    {
        m_granted.TrySetException(failure);
    }

    /// <summary>
    /// The package's problems have all ended: its task ends as a pool's run did. A failure that is
    /// not a cancel first; then a cancelled token; then a cancel a problem threw by itself; else done.
    /// </summary>
    public void Finish()
    {
        if (m_failure != null && m_failure is not OperationCanceledException)
            m_done.TrySetException(m_failure);
        else if (Token.IsCancellationRequested)
            m_done.TrySetCanceled(Token);
        else if (m_failure != null)
            m_done.TrySetException(m_failure);
        else
            m_done.TrySetResult();
    }

    #endregion

    #region IDisposable

    /// <summary>Gives the package's place up (<see cref="SmtSessionBudget.Leave"/>).</summary>
    public void Dispose()
    {
        m_budget.Leave(this);
    }

    #endregion

    #region Properties

    /// <summary>Where the turn stands.</summary>
    public SmtSessionBudgetTurnState State { get; set; }

    /// <summary>The turn's place among those waiting, while it waits.</summary>
    public LinkedListNode<SmtSessionBudgetTurn>? Place { get; set; }

    /// <summary>The registration of the token the turn watches: while it waits, or while it runs.</summary>
    public CancellationTokenRegistration Registration { get; set; }

    /// <summary>Ends when the turn is given.</summary>
    public Task<SmtSessionBudgetTurn> Granted => m_granted.Task;

    /// <summary>Ends when the package's problems have all ended.</summary>
    public Task Completion => m_done.Task;

    /// <summary>The work of one problem; null until the package runs.</summary>
    public Action<int>? Body { get; private set; }

    /// <summary>The package's token.</summary>
    public CancellationToken Token { get; private set; }

    /// <summary>The memory one session of the package claims, bytes.</summary>
    public long SessionBytes { get; private set; }

    /// <summary>The package's problems.</summary>
    public int Count { get; private set; }

    /// <summary>The problems handed out.</summary>
    public int Handed { get; private set; }

    /// <summary>The problems running.</summary>
    public int Running { get; private set; }

    /// <summary>Whether the package has problems to hand out: not all are, and it was neither cancelled nor failed.</summary>
    public bool HasProblemsLeft => !m_halted && Handed < Count;

    #endregion
}
