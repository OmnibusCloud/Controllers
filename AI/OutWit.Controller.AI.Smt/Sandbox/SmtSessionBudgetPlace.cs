namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// What the node's budget of sessions (<see cref="SmtSessionBudget"/>) keeps of one package: where
/// its place stands, the problems it handed out and the ones that run, how it failed. It is the
/// budget's own: the budget makes it, changes it under its lock only, and hands it to nobody. The
/// package holds its turn (<see cref="SmtSessionBudgetTurn"/>), which asks the budget on its
/// behalf and has none of this in its reach, so no code of a package can move a count of the
/// budget, which is the node's for the life of its process.
/// </summary>
internal sealed class SmtSessionBudgetPlace
{
    #region Fields

    private readonly TaskCompletionSource<SmtSessionBudgetTurn> m_granted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource m_done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Exception? m_failure;

    private bool m_halted;

    #endregion

    #region Constructors

    /// <summary>Makes the place of a package in a budget, with the turn the package will hold.</summary>
    /// <param name="budget">The budget.</param>
    public SmtSessionBudgetPlace(SmtSessionBudget budget)
    {
        Turn = new SmtSessionBudgetTurn(budget, this);
    }

    #endregion

    #region Functions

    /// <summary>Sets what the package runs: it is running from now on.</summary>
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

    /// <summary>Hands the next problem out: its index.</summary>
    /// <returns>The index.</returns>
    public int Take()
    {
        Running++;
        return Handed++;
    }

    /// <summary>A problem's session has ended, or never started, with the exception that ended it or null.</summary>
    /// <param name="failure">The exception, or null.</param>
    public void Ended(Exception? failure)
    {
        Running--;
        if (failure != null)
            Fail(failure);
    }

    /// <summary>The package fails: the first failure is kept, and nothing more is handed out.</summary>
    /// <param name="failure">The exception.</param>
    public void Fail(Exception failure)
    {
        m_failure ??= failure;
        m_halted = true;
    }

    /// <summary>Nothing more of the package is handed out (it was cancelled).</summary>
    public void Halt()
    {
        m_halted = true;
    }

    /// <summary>The package is let in: whoever waits for its turn is given it.</summary>
    public void Admit()
    {
        m_granted.TrySetResult(Turn);
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
    /// The package's problems have all ended: its task ends. A failure that is not a cancel first;
    /// then a cancelled token; then a cancel a problem threw by itself; else done.
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

    #region Properties

    /// <summary>The turn the package holds: all it has of the budget.</summary>
    public SmtSessionBudgetTurn Turn { get; }

    /// <summary>Where the place stands.</summary>
    public SmtSessionBudgetPlaceState State { get; set; }

    /// <summary>The place among those waiting, while it waits.</summary>
    public LinkedListNode<SmtSessionBudgetPlace>? Waiting { get; set; }

    /// <summary>The registration of the token the place watches: while it waits, or while it runs.</summary>
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
