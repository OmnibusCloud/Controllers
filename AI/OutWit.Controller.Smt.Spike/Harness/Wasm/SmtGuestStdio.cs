using System.Text;
using OutWit.Controller.Smt.Spike.Protocol;
using Wasmtime;

namespace OutWit.Controller.Smt.Spike.Wasm;

/// <summary>
/// The guest's stdin and stdout as host calls instead of files: fd_read on descriptor 0 hands
/// the guest the driver's next piece of input, asked for at the moment the guest has consumed
/// everything before it (the solver has then answered all of it: its C++ stdin is tied to its
/// stdout, so the answers were flushed before it asked); fd_write on 1 and 2 collects what the
/// guest writes. This is what makes the session interactive inside the sandbox (an enumerate
/// loop, a reason asked only after an unknown) without a thread or a pipe: the driver runs
/// inside the guest's own read call, so the run stays one deterministic sequence.
/// <para>
/// Every other descriptor answers EBADF on these two calls (the guest is given no files).
/// </para>
/// </summary>
internal sealed class SmtGuestStdio
{
    #region Constants

    private const string WASI_MODULE = "wasi_snapshot_preview1";
    private const string GUEST_MEMORY = "memory";
    private const int ERRNO_SUCCESS = 0;
    private const int ERRNO_BADF = 8;
    private const int ERRNO_FAULT = 21;
    private const int IOVEC_BYTES = 8;

    #endregion

    #region Fields

    private readonly SmtSessionDriver m_driver;
    private readonly long m_fuelBudget;
    private readonly int m_stdoutLimit;
    private readonly int m_stderrLimit;
    private readonly MemoryStream m_stdout = new();
    private readonly MemoryStream m_stderr = new();
    private byte[] m_pending = [];
    private int m_offset;
    private bool m_endOfInput;

    #endregion

    #region Constructors

    /// <summary>Makes the guest's stdio for one session.</summary>
    /// <param name="driver">The protocol driver asked for input.</param>
    /// <param name="fuelBudget">The fuel the guest was given after instantiation (to tell the driver what it used).</param>
    /// <param name="stdoutLimit">The most stdout bytes kept; beyond it the session ends (output exceeded).</param>
    /// <param name="stderrLimit">The most stderr bytes kept.</param>
    public SmtGuestStdio(SmtSessionDriver driver, long fuelBudget, int stdoutLimit, int stderrLimit)
    {
        m_driver = driver;
        m_fuelBudget = fuelBudget;
        m_stdoutLimit = stdoutLimit;
        m_stderrLimit = stderrLimit;
    }

    #endregion

    #region Functions

    /// <summary>Puts the two calls into a linker (which must allow shadowing; WASI is already defined in it).</summary>
    /// <param name="linker">The run's linker.</param>
    public void Define(Linker linker)
    {
        linker.DefineFunction(WASI_MODULE, "fd_read",
            (Caller caller, int fd, int iovs, int iovsLength, int readPtr) => Read(caller, fd, iovs, iovsLength, readPtr));
        linker.DefineFunction(WASI_MODULE, "fd_write",
            (Caller caller, int fd, int iovs, int iovsLength, int writtenPtr) => Write(caller, fd, iovs, iovsLength, writtenPtr));
    }

    private int Read(Caller caller, int fd, int iovs, int iovsLength, int readPtr)
    {
        if (fd != 0)
            return ERRNO_BADF;

        var memory = caller.GetMemory(GUEST_MEMORY);
        if (memory == null)
            return ERRNO_FAULT;

        if (m_offset >= m_pending.Length && !m_endOfInput)
        {
            InputRequests++;
            var fuelUsed = m_fuelBudget - (long)caller.Fuel;
            if (InputRequests == 1)
            {
                FuelAtFirstRead = fuelUsed;
                MsAtFirstRead = Clock?.Elapsed.TotalMilliseconds ?? 0;
            }

            var next = OutputExceeded ? null : m_driver.Next(Encoding.UTF8.GetString(m_stdout.GetBuffer(), 0, (int)m_stdout.Length), fuelUsed);
            if (next == null)
            {
                m_endOfInput = true;
                m_pending = [];
            }
            else
            {
                m_pending = Encoding.UTF8.GetBytes(next);
                InputBytes += m_pending.Length;
            }

            m_offset = 0;
        }

        var total = 0;
        for (var i = 0; i < iovsLength && m_offset < m_pending.Length; i++)
        {
            var buffer = memory.ReadInt32(iovs + i * IOVEC_BYTES);
            var length = memory.ReadInt32(iovs + i * IOVEC_BYTES + 4);
            var count = Math.Min(length, m_pending.Length - m_offset);
            if (count <= 0)
                continue;

            m_pending.AsSpan(m_offset, count).CopyTo(memory.GetSpan(buffer, count));
            m_offset += count;
            total += count;
        }

        memory.WriteInt32(readPtr, total);
        return ERRNO_SUCCESS;
    }

    private int Write(Caller caller, int fd, int iovs, int iovsLength, int writtenPtr)
    {
        if (fd != 1 && fd != 2)
            return ERRNO_BADF;

        var memory = caller.GetMemory(GUEST_MEMORY);
        if (memory == null)
            return ERRNO_FAULT;

        var sink = fd == 1 ? m_stdout : m_stderr;
        var limit = fd == 1 ? m_stdoutLimit : m_stderrLimit;
        var total = 0;
        for (var i = 0; i < iovsLength; i++)
        {
            var buffer = memory.ReadInt32(iovs + i * IOVEC_BYTES);
            var length = memory.ReadInt32(iovs + i * IOVEC_BYTES + 4);
            if (length <= 0)
                continue;

            var room = (int)Math.Max(0, limit - sink.Length);
            var kept = Math.Min(room, length);
            if (kept > 0)
                sink.Write(memory.GetSpan(buffer, kept));
            if (kept < length)
            {
                if (fd == 1)
                    OutputExceeded = true;
                else
                    StderrTruncated = true;
            }

            total += length;
        }

        memory.WriteInt32(writtenPtr, total);
        return ERRNO_SUCCESS;
    }

    #endregion

    #region Properties

    /// <summary>Everything the guest wrote to stdout (up to the limit).</summary>
    public byte[] Stdout => m_stdout.ToArray();

    /// <summary>Everything the guest wrote to stderr (up to the limit), as text.</summary>
    public string Stderr => Encoding.UTF8.GetString(m_stderr.GetBuffer(), 0, (int)m_stderr.Length);

    /// <summary>Whether stdout passed its limit (the driver was then given no more turns).</summary>
    public bool OutputExceeded { get; private set; }

    /// <summary>Whether stderr passed its limit.</summary>
    public bool StderrTruncated { get; private set; }

    /// <summary>How many times the guest asked for input with nothing pending (driver turns).</summary>
    public int InputRequests { get; private set; }

    /// <summary>The bytes of input handed to the guest.</summary>
    public long InputBytes { get; private set; }

    /// <summary>The session's clock, read at the guest's first request for input; may be null.</summary>
    public System.Diagnostics.Stopwatch? Clock { get; init; }

    /// <summary>The clock when the guest first asked for input: the solver's own start-up time.</summary>
    public double MsAtFirstRead { get; private set; }

    /// <summary>The fuel the guest used before it first asked for input: the solver's own start-up.</summary>
    public long FuelAtFirstRead { get; private set; }

    #endregion
}
