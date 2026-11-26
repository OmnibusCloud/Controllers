using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// The sessions a node runs at once, shared by the problems of every package its process solves.
/// The client starts every package of a node's share together; a package that had sessions of its
/// own kept them until its slowest problem ended while the next package waited, so on a long tail
/// most of the node stood idle. Here a session that ends goes to the next problem waiting, of the
/// same package or of the next one.
/// <para>
/// <b>The budget.</b> At most <c>min(cores, 16)</c> sessions run at once, and together they claim
/// at most three quarters of the memory the machine had available (<see cref="SmtSandboxDegree"/>),
/// each counted at its package's session size (<see cref="SmtSandboxDegree.SessionBytes(SmtOptionsData)"/>).
/// For packages of one size that is the degree rule to the session: <c>min(cores, 16, usable /
/// session)</c>, what one package had on its own. Packages of different sizes (of different jobs)
/// share the same bytes: a large session takes the room of several small ones, and no mix of them
/// claims more than the memory holds, which a degree per size could not promise.
/// </para>
/// <para>
/// <b>The order.</b> Problems are handed out in the order their packages were let in, a package's
/// own in its order. The first problem waiting goes first and nothing overtakes it: a large session
/// waiting for room is not starved by small ones behind it.
/// </para>
/// <para>
/// <b>Open packages.</b> A package waits for its turn (<see cref="EnterAsync"/>) with its blob
/// downloaded. It is let in (checked, then opened: its problems read, its rows written) only when
/// every package open has handed out all of its problems, so the next package is ready while the
/// last problems of the others run. At most one open package has problems still to give, and every
/// other one has a session running: no more packages are open than sessions running and one. A
/// package's blob is read a problem at a time and closed with its last one; its result package
/// stays open until its last row (a Brotli encoder, about 11 MiB). <see cref="MAX_OPEN_PACKAGES"/>
/// bounds them besides.
/// </para>
/// <para>
/// <b>The machine.</b> The cores and the available memory are read when a package is let in and no
/// other package is open: the first package of a share, after the module is compiled. While
/// packages are open the reading stands: what the machine has available then counts the sessions
/// running, which the budget already holds.
/// </para>
/// <para>
/// <b>What a package holds.</b> A package is given its turn (<see cref="SmtSessionBudgetTurn"/>):
/// the degree, the run, the leave. What the budget counts of it is kept apart
/// (<see cref="SmtSessionBudgetPlace"/>), changed under the budget's lock and handed to nobody.
/// </para>
/// <para>
/// The sessions run on threads made for them below normal priority, each with a stack that holds a
/// guest's (<see cref="WasmHostGuestThread"/>, <see cref="SmtWorkerPool.WORKER_PRIORITY"/>): a
/// thread goes on to the next problem while there is one it may start, and ends when there is none.
/// </para>
/// </summary>
internal sealed class SmtSessionBudget
{
    #region Constants

    /// <summary>
    /// The most packages open in a process at once. A package is opened only when the others have
    /// handed out all of their problems, so the open ones are never more than the sessions running
    /// and one: on a machine of one session at most two, of sixteen at most seventeen. The cap is
    /// the most sessions a node runs (<see cref="SmtSandboxDegree.MAX_DEGREE"/>), so it binds only
    /// where every session is busy already: a cap below the sessions would leave sessions idle while
    /// packages wait, whenever more packages than the cap are in their tails at once, as packages of
    /// problems from one second to the hard cap are. What an open package holds is its result
    /// package's encoder, about 11 MiB, beside sessions of 2,752 MiB each by default.
    /// </summary>
    public const int MAX_OPEN_PACKAGES = SmtSandboxDegree.MAX_DEGREE;

    private const string WORKER_NAME = "smt-session";

    #endregion

    #region Fields

    private readonly object m_gate = new();

    private readonly Func<(int Cores, long AvailableBytes)> m_read;

    private readonly Func<Action, Thread> m_makeThread;

    private readonly int m_maxOpen;

    private readonly LinkedList<SmtSessionBudgetPlace> m_waiting = new();

    private readonly List<SmtSessionBudgetPlace> m_open = new();

    private bool m_hasReading;

    private int m_cores;

    private long m_availableBytes;

    private int m_sessionCap;

    private long m_capacityBytes;

    private int m_running;

    private long m_runningBytes;

    private int m_workers;

    private int m_threads;

    private int m_reads;

    private int m_peakRunning;

    private long m_peakRunningBytes;

    private int m_peakOpen;

    #endregion

    #region Constructors

    /// <summary>The budget of a machine that is read (<see cref="Node"/> on a node).</summary>
    /// <param name="read">Reads the machine's logical cores and its available memory (0 or less when it cannot say).</param>
    /// <param name="maxOpen">The most packages open at once.</param>
    /// <param name="makeThread">Makes the thread a session runs on, not started; null for a thread that holds a guest's stack (<see cref="WasmHostGuestThread.Create"/>).</param>
    public SmtSessionBudget(Func<(int Cores, long AvailableBytes)> read, int maxOpen = MAX_OPEN_PACKAGES, Func<Action, Thread>? makeThread = null)
    {
        m_read = read ?? throw new ArgumentNullException(nameof(read));
        m_maxOpen = Math.Max(1, maxOpen);
        m_makeThread = makeThread ?? WasmHostGuestThread.Create;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Waits for a package's turn: the turn is given when the packages open have handed out all of
    /// their problems and fewer than <see cref="MAX_OPEN_PACKAGES"/> are open, in the order the
    /// packages asked. The turn is the package's place until it is disposed.
    /// </summary>
    /// <param name="cancellationToken">Gives the place up while it is waited for.</param>
    /// <returns>The turn.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled before the turn came.</exception>
    public Task<SmtSessionBudgetTurn> EnterAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<SmtSessionBudgetTurn>(cancellationToken);

        var place = new SmtSessionBudgetPlace(this);
        lock (m_gate)
        {
            place.Waiting = m_waiting.AddLast(place);
            Grant();

            if (place.State == SmtSessionBudgetPlaceState.Waiting)
                place.Registration = cancellationToken.Register(() => GiveUp(place, cancellationToken));
        }

        return place.Granted;
    }

    /// <summary>
    /// The degree rule for a session size on the budget's reading of the machine
    /// (<see cref="SmtSandboxDegree.Compute"/>), for the package of a place; the machine is read
    /// again when no other package is open. A package asks through its turn
    /// (<see cref="SmtSessionBudgetTurn.Degree"/>).
    /// </summary>
    /// <param name="place">The package's place, let in and not yet running.</param>
    /// <param name="sessionBytes">The memory one session of the package may take.</param>
    /// <returns>The sessions of that size at once, and what decided it; 0 when not one fits.</returns>
    public SmtDegree Degree(SmtSessionBudgetPlace place, long sessionBytes)
    {
        lock (m_gate)
        {
            if (!m_hasReading || m_open.TrueForAll(open => ReferenceEquals(open, place)))
                Read();

            return SmtSandboxDegree.Compute(m_cores, m_availableBytes, sessionBytes);
        }
    }

    /// <summary>
    /// Hands the problems of a place's package out to the budget's sessions. A package asks through
    /// its turn (<see cref="SmtSessionBudgetTurn.RunAsync"/>).
    /// </summary>
    /// <param name="place">The package's place, let in and not yet running.</param>
    /// <param name="count">The problems.</param>
    /// <param name="sessionBytes">The memory one session of the package may take.</param>
    /// <param name="body">The work of one problem; runs on a session's thread, concurrently with itself.</param>
    /// <param name="cancellationToken">Once cancelled, no further problem of the package starts; those running finish, then the task ends cancelled.</param>
    /// <returns>A task that ends when every problem handed out has ended.</returns>
    /// <exception cref="InvalidOperationException">The place is not one let in and not yet running.</exception>
    public Task Run(SmtSessionBudgetPlace place, int count, long sessionBytes, Action<int> body, CancellationToken cancellationToken)
    {
        lock (m_gate)
        {
            if (place.State != SmtSessionBudgetPlaceState.Open || place.Body != null)
                throw new InvalidOperationException("a package runs once, in the turn it was given");

            // A package that did not ask for its degree: the machine is read all the same.
            if (!m_hasReading)
                Read();

            place.Start(count, sessionBytes, body, cancellationToken);
            if (!TryComplete(place))
            {
                place.Registration = cancellationToken.Register(() => Stop(place));
                Pump();
            }

            Grant();
        }

        return place.Completion;
    }

    /// <summary>
    /// Gives a place up: one waited for or let in and not running leaves at once; a running one
    /// hands out nothing more and leaves when its sessions have ended. A package asks by disposing
    /// its turn.
    /// </summary>
    /// <param name="place">The place.</param>
    public void Leave(SmtSessionBudgetPlace place)
    {
        lock (m_gate)
        {
            switch (place.State)
            {
                case SmtSessionBudgetPlaceState.Waiting:
                    m_waiting.Remove(place.Waiting!);
                    place.State = SmtSessionBudgetPlaceState.Left;
                    place.Registration.Unregister();
                    place.Refuse(new ObjectDisposedException(nameof(SmtSessionBudgetTurn)));
                    break;

                case SmtSessionBudgetPlaceState.Open when place.Body == null:
                    m_open.Remove(place);
                    place.State = SmtSessionBudgetPlaceState.Left;
                    break;

                case SmtSessionBudgetPlaceState.Open:
                    place.Fail(new InvalidOperationException("the package left the budget before its problems ended"));
                    TryComplete(place);
                    break;
            }

            Grant();
        }
    }

    private void Read()
    {
        var (cores, available) = m_read();
        m_hasReading = true;
        m_reads++;
        m_cores = Math.Max(1, cores);
        m_availableBytes = Math.Max(0, available);
        m_sessionCap = Math.Min(m_cores, SmtSandboxDegree.MAX_DEGREE);

        // The same share of the same reading as the degree rule, so that sessions of one size fit
        // exactly as many times as the rule gives.
        m_capacityBytes = m_availableBytes > 0 ? (long)(m_availableBytes * SmtSandboxDegree.RAM_HEADROOM) : 0;
    }

    private bool Fits(long sessionBytes)
    {
        return m_running < m_sessionCap && (m_capacityBytes <= 0 || m_runningBytes + sessionBytes <= m_capacityBytes);
    }

    /// <summary>Lets packages in while one may be: fewer than the cap open, and none of them with problems still to give.</summary>
    private void Grant()
    {
        while (m_waiting.First is { } first && m_open.Count < m_maxOpen && m_open.TrueForAll(open => open.Body != null && !open.HasProblemsLeft))
        {
            var place = first.Value;
            m_waiting.RemoveFirst();
            place.Registration.Unregister();
            place.State = SmtSessionBudgetPlaceState.Open;
            m_open.Add(place);
            m_peakOpen = Math.Max(m_peakOpen, m_open.Count);
            place.Admit();
        }
    }

    /// <summary>Starts a session for every problem that may start now.</summary>
    private void Pump()
    {
        while (TryTake(out var place, out var index))
            StartWorker(place!, index);
    }

    /// <summary>Takes the first problem waiting, when its session fits now; nothing overtakes it.</summary>
    private bool TryTake(out SmtSessionBudgetPlace? place, out int index)
    {
        for (var position = 0; position < m_open.Count; position++)
        {
            var candidate = m_open[position];
            if (candidate.Body == null || !candidate.HasProblemsLeft)
                continue;

            if (candidate.Token.IsCancellationRequested)
            {
                candidate.Halt();
                if (TryComplete(candidate))
                    position--;

                continue;
            }

            if (!Fits(candidate.SessionBytes))
            {
                if (m_running > 0)
                    break;

                // Nothing runs and its session still does not fit: it never will. A package asks
                // for its degree first and is refused so (memory_insufficient); this is the
                // budget's own guard against a wait without end.
                candidate.Fail(new InvalidOperationException("not one session of the package fits in the budget"));
                if (TryComplete(candidate))
                    position--;

                continue;
            }

            index = candidate.Take();
            m_running++;
            m_runningBytes += candidate.SessionBytes;
            m_peakRunning = Math.Max(m_peakRunning, m_running);
            m_peakRunningBytes = Math.Max(m_peakRunningBytes, m_runningBytes);
            place = candidate;
            return true;
        }

        place = null;
        index = -1;
        return false;
    }

    /// <summary>Ends a running package whose problems are all handed out and ended: it leaves the open ones.</summary>
    private bool TryComplete(SmtSessionBudgetPlace place)
    {
        if (place.State != SmtSessionBudgetPlaceState.Open || place.Body == null || place.HasProblemsLeft || place.Running > 0)
            return false;

        m_open.Remove(place);
        place.State = SmtSessionBudgetPlaceState.Left;
        place.Registration.Unregister();
        place.Finish();
        return true;
    }

    /// <summary>
    /// Makes and starts the thread of a session for a problem that was just taken. A machine that
    /// has no thread to give, whatever it says of it (no memory for the stack, a thread that could
    /// not start), fails that problem's package and nothing else: the problem was counted as
    /// running when it was taken and never ran, so the counts are put back here. Nothing may leave
    /// this method: it runs under the budget's lock, and also on a session's own thread, where an
    /// exception nobody catches ends the process.
    /// </summary>
    private void StartWorker(SmtSessionBudgetPlace place, int index)
    {
        try
        {
            var thread = m_makeThread(() => Work(place, index));
            thread.IsBackground = true;
            thread.Name = $"{WORKER_NAME}-{m_threads++}";
            thread.Priority = SmtWorkerPool.WORKER_PRIORITY;
            thread.Start();
            m_workers++;
        }
        catch (Exception exception)
        {
            m_running--;
            m_runningBytes -= place.SessionBytes;
            place.Ended(exception);
            TryComplete(place);
        }
    }

    /// <summary>A session's thread: runs a problem, then the next one that may start, until there is none.</summary>
    private void Work(SmtSessionBudgetPlace place, int index)
    {
        while (true)
        {
            Exception? failure = null;
            try
            {
                place.Body!(index);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            lock (m_gate)
            {
                m_running--;
                m_runningBytes -= place.SessionBytes;
                place.Ended(failure);
                TryComplete(place);

                var more = TryTake(out var next, out var nextIndex);
                Pump();
                Grant();

                if (!more)
                {
                    m_workers--;
                    return;
                }

                place = next!;
                index = nextIndex;
            }
        }
    }

    private void Stop(SmtSessionBudgetPlace place)
    {
        lock (m_gate)
        {
            if (place.State != SmtSessionBudgetPlaceState.Open || place.Body == null)
                return;

            place.Halt();
            TryComplete(place);
            Pump();
            Grant();
        }
    }

    private void GiveUp(SmtSessionBudgetPlace place, CancellationToken cancellationToken)
    {
        lock (m_gate)
        {
            if (place.State != SmtSessionBudgetPlaceState.Waiting)
                return;

            m_waiting.Remove(place.Waiting!);
            place.State = SmtSessionBudgetPlaceState.Left;
            place.Cancel(cancellationToken);
        }
    }

    private T Locked<T>(Func<T> read)
    {
        lock (m_gate)
            return read();
    }

    #endregion

    #region Properties

    /// <summary>The process's budget: the node's cores and memory, shared by every package the process solves.</summary>
    public static SmtSessionBudget Node { get; } = new(() => (Environment.ProcessorCount, SmtSandboxDegree.AvailableBytes()));

    /// <summary>The sessions running now.</summary>
    public int Running => Locked(() => m_running);

    /// <summary>The memory the sessions running now claim, bytes.</summary>
    public long RunningBytes => Locked(() => m_runningBytes);

    /// <summary>The packages open now: let in, and not yet ended.</summary>
    public int Open => Locked(() => m_open.Count);

    /// <summary>The packages waiting for their turn now.</summary>
    public int Waiting => Locked(() => m_waiting.Count);

    /// <summary>The session threads alive now.</summary>
    public int Workers => Locked(() => m_workers);

    /// <summary>How many times the machine was read.</summary>
    public int Reads => Locked(() => m_reads);

    /// <summary>The most sessions that ran at once.</summary>
    public int PeakRunning => Locked(() => m_peakRunning);

    /// <summary>The most memory the sessions running at once claimed, bytes.</summary>
    public long PeakRunningBytes => Locked(() => m_peakRunningBytes);

    /// <summary>The most packages open at once.</summary>
    public int PeakOpen => Locked(() => m_peakOpen);

    /// <summary>The most sessions at once by the last reading: <c>min(cores, 16)</c>.</summary>
    public int SessionCap => Locked(() => m_sessionCap);

    /// <summary>The memory the sessions may claim together by the last reading, bytes; 0 when it is not counted.</summary>
    public long CapacityBytes => Locked(() => m_capacityBytes);

    #endregion
}
