using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The guest's stdin in a run that holds a conversation
/// (<see cref="WasmHostRunRequest.Conversation"/>): descriptor 0, read from what the caller gives
/// turn by turn (<see cref="IWasmHostConversation"/>). The read is the host's own call, as the
/// writes of stdout and stderr are (<see cref="WasmHostSyscalls"/>), and it keeps to their rules:
/// <list type="bullet">
/// <item>it fills the first buffer of the guest's list that has room, as WASI's own read does,
/// with no more than the buffer holds and no more than is pending;</item>
/// <item>a pointer outside the guest's memory answers "bad address", and the caller is not asked
/// for input that such a read could not take;</item>
/// <item>when the run asks for it, the read costs fuel: the call, and a unit a byte handed over
/// (<see cref="WasmHostMeter"/>).</item>
/// </list>
/// The caller is asked when a read finds nothing pending, and is shown then what the guest wrote
/// to stdout since it was last asked: the bytes the stream's cap kept
/// (<see cref="WasmHostOutput.KeptFrom"/>), so a guest that floods its stdout shows the caller no
/// more than the cap. An empty answer ends the input for good. A guest that closes descriptor 0
/// has ended the conversation itself: the number stands for whatever the guest opens under it
/// next.
/// <para>
/// Nothing else of a run changes: a read of any other descriptor goes the way it goes without a
/// conversation, and a run without a conversation has none of this defined.
/// </para>
/// </summary>
internal sealed class WasmHostConversationInput
{
    #region Constants

    /// <summary>The descriptor a conversation feeds: the guest's stdin.</summary>
    public const int DESCRIPTOR = 0;

    /// <summary>The words a failure of the caller's conversation is wrapped in when its type is one the host reads a run's end by.</summary>
    public const string CALLER_FAULT = "the run's conversation failed";

    private const string WASI_MODULE = "wasi_snapshot_preview1";

    #endregion

    #region Fields

    private readonly IWasmHostConversation m_conversation;
    private readonly WasmHostOutput m_stdout;
    private readonly WasmHostMeter m_meter;
    private readonly WasmHostWasi m_wasi;

    private ReadOnlyMemory<byte> m_pending;
    private long m_seen;
    private bool m_ended;
    private bool m_closed;

    #endregion

    #region Constructors

    /// <summary>
    /// The stdin of one run.
    /// </summary>
    /// <param name="conversation">The caller's side.</param>
    /// <param name="stdout">The run's stdout as the host keeps it.</param>
    /// <param name="meter">The run's meter.</param>
    /// <param name="wasi">The run's way to WASI's own calls.</param>
    public WasmHostConversationInput(IWasmHostConversation conversation, WasmHostOutput stdout, WasmHostMeter meter, WasmHostWasi wasi)
    {
        m_conversation = conversation;
        m_stdout = stdout;
        m_meter = meter;
        m_wasi = wasi;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Puts the run's <c>fd_read</c> over the one the linker has (WASI's own, or the host's
    /// metered one): descriptor 0 is answered here, and any other descriptor is read as in a run
    /// without a conversation, by the host's metered read when the run's calls cost fuel and by
    /// WASI as the call came when they do not.
    /// </summary>
    /// <param name="linker">The run's linker, which has WASI and allows shadowing.</param>
    /// <param name="metered">The host's metered read: the guest's call, the descriptor, the list of buffers, their number, the offset (none) and where the number of bytes goes.</param>
    /// <param name="note">The host's look at an exception on its way out of one of its calls; it answers false.</param>
    public void Define(Linker linker, Func<Caller, int, int, int, long?, int, int> metered, Func<Exception, bool> note)
    {
        linker.DefineFunction(WASI_MODULE, WasmHostShim.FD_READ, (Caller caller, int descriptor, int buffers, int count, int read) =>
        {
            try
            {
                if (Holds(descriptor))
                    return Read(caller, buffers, count, read);

                return m_meter.Charges
                    ? metered(caller, descriptor, buffers, count, null, read)
                    : m_wasi.Forward(caller, () => m_wasi.FdRead!(descriptor, buffers, count, read));
            }
            catch (Exception exception) when (note(exception)) { throw; }
        });
    }

    #endregion

    #region Functions

    /// <summary>
    /// Refuses a run whose request cannot hold its conversation: one that gives a stdin text as
    /// well (a guest has one stdin), or sends its stdout to a file (the conversation reads the
    /// stdout the host keeps in memory). A request without a conversation passes as it is.
    /// </summary>
    /// <param name="request">The run.</param>
    /// <exception cref="ArgumentException">The request has a conversation together with a stdin text or a file for its stdout.</exception>
    public static void ThrowIfRefused(WasmHostRunRequest request)
    {
        if (request.Conversation == null)
            return;

        if (request.Stdin != null)
            throw new ArgumentException("a run has a conversation or a stdin text, not both", nameof(request));

        if (request.StdoutFile != null)
            throw new ArgumentException("a run with a conversation keeps its stdout in memory, it cannot go to a file", nameof(request));
    }

    /// <summary>Notes that the guest closed a descriptor: closing its stdin ends the conversation.</summary>
    /// <param name="descriptor">The descriptor WASI closed.</param>
    public void Closed(int descriptor)
    {
        if (descriptor == DESCRIPTOR)
            m_closed = true;
    }

    /// <summary>Whether a read is answered here: one of the guest's stdin, until the guest closes it.</summary>
    private bool Holds(int descriptor)
    {
        return descriptor == DESCRIPTOR && !m_closed;
    }

    /// <summary>
    /// The guest's <c>fd_read</c> of descriptor 0: the next pending bytes into the first buffer
    /// that has room, the caller asked first when nothing is pending. Answers WASI's errno:
    /// success (with zero bytes at the end of input), "invalid" for a list too long, "bad address"
    /// for a pointer outside the memory.
    /// </summary>
    private int Read(Caller caller, int buffers, int count, int read)
    {
        m_meter.Call(caller, WasmHostShim.FD_READ);

        var memory = caller.GetMemory(WasmHostShim.MEMORY);
        if (memory == null)
            return WasmHostErrno.FAULT;

        // As WASI's own read does, one call fills the first buffer that has room.
        var errno = WasmHostMemory.FirstBuffer(memory, buffers, count, out var pointer, out var length);
        if (errno != WasmHostErrno.SUCCESS)
            return errno;

        if (!WasmHostMemory.InRange(memory, read, sizeof(int)))
            return WasmHostErrno.FAULT;

        // Only a read that can take an answer asks for one: the pointers are checked by now, and
        // a read without room takes nothing and leaves the turn to the next.
        var given = 0;
        if (length > 0)
        {
            if (m_pending.IsEmpty && !m_ended)
                Ask();

            given = Math.Min(length, m_pending.Length);
        }

        if (given > 0)
        {
            m_meter.Bytes(caller, WasmHostShim.FD_READ, given);
            m_pending.Span[..given].CopyTo(memory.GetSpan(pointer, given));
            m_pending = m_pending[given..];
        }

        memory.WriteInt32((uint)read, given);
        return WasmHostErrno.SUCCESS;
    }

    /// <summary>
    /// Asks the caller for the next input and shows it what stdout kept since the last time.
    /// </summary>
    private void Ask()
    {
        var output = m_stdout.KeptFrom(m_seen);
        m_seen += output.Length;

        try
        {
            m_pending = m_conversation.Next(output);
        }
        catch (Exception exception) when (exception is WasmHostRunEnded or WasmtimeException)
        {
            // The host reads a run's end by these two types (its own way out of the guest, the
            // engine's error): the caller's failure must not pass for either.
            throw new InvalidOperationException(CALLER_FAULT, exception);
        }

        m_ended = m_pending.IsEmpty;
    }

    #endregion
}
