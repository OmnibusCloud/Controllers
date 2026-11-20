using System.Buffers.Binary;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// A WASI linker whose time and randomness are the run's own, so a program's execution —
/// output and fuel — is reproducible on any node: the integrity foundation a controller
/// compares against.
///
/// The clock starts at a pinned instant and moves only when the program waits: a sleep
/// (poll_oneoff) returns at once and the clock reads that much later; a wait of no length
/// still takes a nanosecond, as every real one takes some time. Nothing of the host's time
/// reaches the program, and no wait holds a worker. Randomness and the file calls are the
/// run's own too (<see cref="WasmHostSyscalls"/>).
///
/// Four calls are taken away, each for a stated reason: renumbering a descriptor, listing a
/// folder, and setting a file's times (by descriptor and by path) answer with an error and
/// do nothing.
///
/// Every call defined here costs the run what any call into the host costs, and a wait costs the
/// bytes of the records it reads and writes besides (<see cref="WasmHostMeter"/>): a wait returns
/// at once, so a program could otherwise ask for thousands of them a second for almost no fuel.
/// </summary>
internal static class WasmHostLinker
{
    #region Constants

    /// <summary>The instant every run's clock starts at (nanoseconds).</summary>
    public const long PINNED_CLOCK_NS = 0;

    /// <summary>
    /// What a wait takes at the least (nanoseconds). An interpreter that sleeps until the
    /// clock is past its deadline (Ruby: "now > end") asks for a wait of no length when the
    /// clock stands exactly on it; if that wait took nothing, the sleep would never end.
    /// </summary>
    private const long SHORTEST_WAIT_NS = 1;

    private const string WASI_MODULE = "wasi_snapshot_preview1";
    private const string GUEST_MEMORY = "memory";
    private const string CLOCK_TIME_GET = "clock_time_get";
    private const string POLL_ONEOFF = "poll_oneoff";
    private const string FD_RENUMBER = "fd_renumber";
    private const string FD_READDIR = "fd_readdir";
    private const string FD_FILESTAT_SET_TIMES = "fd_filestat_set_times";
    private const string PATH_FILESTAT_SET_TIMES = "path_filestat_set_times";
    private const int ERRNO_SUCCESS = 0;
    private const int ERRNO_ACCES = 2;
    private const int ERRNO_FAULT = 21;
    private const int ERRNO_INVAL = 28;
    private const int ERRNO_NOSYS = 52;
    private const int ERRNO_ROFS = 69;

    // poll_oneoff's records (WASI preview 1). A subscription is 48 bytes: the userdata at 0,
    // the kind at 8 (0 = clock, 1 = descriptor readable, 2 = descriptor writable) and, for a
    // clock, the timeout at 24 and the flags at 40 (bit 0: the timeout is an instant, not a
    // duration). An event is 32 bytes: the userdata at 0, the error at 8, the kind at 10.
    internal const int SUBSCRIPTION_SIZE = 48;
    private const int SUBSCRIPTION_KIND_OFFSET = 8;
    private const int SUBSCRIPTION_TIMEOUT_OFFSET = 24;
    private const int SUBSCRIPTION_FLAGS_OFFSET = 40;
    private const byte SUBSCRIPTION_KIND_CLOCK = 0;
    private const ushort SUBSCRIPTION_FLAG_ABSOLUTE = 1;
    internal const int EVENT_SIZE = 32;
    private const int EVENT_KIND_OFFSET = 10;
    internal const int MAX_SUBSCRIPTIONS = 4096;

    #endregion

    #region Functions

    /// <summary>
    /// Creates the linker for one run.
    /// </summary>
    /// <param name="engine">The host's engine.</param>
    /// <param name="meter">The run's meter.</param>
    /// <returns>A linker with WASI defined and the run's own time in it; the caller disposes it.</returns>
    public static Linker Create(WasmEngine engine, WasmHostMeter meter)
    {
        var linker = new Linker(engine) { AllowShadowing = true };
        linker.DefineWasi();

        // The run's clock, the same for every clock id: it reads the pinned instant until the
        // program waits. Removes the last source of fuel non-determinism found in the runtime
        // spike, and with it any sight of the host's time. The pointer is the guest's: one
        // outside its memory answers "bad address".
        var clock = new GuestClock(PINNED_CLOCK_NS);
        linker.DefineFunction(WASI_MODULE, CLOCK_TIME_GET,
            (Caller caller, int clockId, long precision, int timePtr) =>
            {
                meter.Call(caller, CLOCK_TIME_GET);

                var memory = caller.GetMemory(GUEST_MEMORY);
                if (memory == null || (long)(uint)timePtr + sizeof(long) > memory.GetLength())
                    return ERRNO_FAULT;

                memory.WriteInt64((uint)timePtr, clock.Now);
                return ERRNO_SUCCESS;
            });

        // A wait is a call into the host, where neither fuel nor the wall limit reaches: the
        // real one would hold a worker for as long as the program asked. Here it returns at
        // once and moves the run's clock instead.
        linker.DefineFunction(WASI_MODULE, POLL_ONEOFF,
            (Caller caller, int subscriptionsPtr, int eventsPtr, int count, int storedPtr) =>
            {
                meter.Call(caller, POLL_ONEOFF);
                return Poll(caller, meter, clock, subscriptionsPtr, eventsPtr, count, storedPtr);
            });

        // No descriptor juggling. Nothing a guest does needs fd_renumber (the C library's dup2),
        // and it is where wasmtime up to 44.0.2 leaked host descriptors (GHSA-3p27-qvp9-27qf):
        // it answers "not implemented".
        linker.DefineFunction(WASI_MODULE, FD_RENUMBER, (Caller caller, int from, int to) => Answer(caller, meter, FD_RENUMBER, ERRNO_NOSYS));

        // No directory listing. The order of a listing is the host file system's (two nodes
        // would print it differently), and before wasmtime 48.0.4 every entry carried three
        // bytes of uninitialized host memory to the guest (GHSA-96f6-r43r-8c24). "Permission
        // denied" is what the interpreters' own importers take for an empty folder.
        linker.DefineFunction(WASI_MODULE, FD_READDIR,
            (Caller caller, int fd, int buffer, int bufferLength, long cookie, int usedPtr) => Answer(caller, meter, FD_READDIR, ERRNO_ACCES));

        // No file times to set: the times of a file are the host's clock, which no run may see
        // or leave behind, and before wasmtime 48.0.3 an out-of-range time panicked the host
        // (GHSA-j2g9-4prp-pf6h).
        linker.DefineFunction(WASI_MODULE, FD_FILESTAT_SET_TIMES,
            (Caller caller, int fd, long accessed, long modified, int flags) => Answer(caller, meter, FD_FILESTAT_SET_TIMES, ERRNO_ROFS));
        linker.DefineFunction(WASI_MODULE, PATH_FILESTAT_SET_TIMES,
            (Caller caller, int fd, int lookupFlags, int pathPtr, int pathLength, long accessed, long modified, int flags) => Answer(caller, meter, PATH_FILESTAT_SET_TIMES, ERRNO_ROFS));

        return linker;
    }

    /// <summary>A call the host answers with one fixed answer: paid for like any call.</summary>
    private static int Answer(Caller caller, WasmHostMeter meter, string name, int errno)
    {
        meter.Call(caller, name);
        return errno;
    }

    /// <summary>
    /// poll_oneoff without waiting. A descriptor is always ready (stdin is a file, stdout and
    /// stderr are files), so when the program asks about any, those are answered and no time
    /// passes. When it asks for timers only, the clock moves to the earliest of them (by a
    /// nanosecond at the least) and that one, with any other due by then, is answered. The
    /// records cost their bytes: every subscription read, every event written.
    /// </summary>
    private static int Poll(Caller caller, WasmHostMeter meter, GuestClock clock, int subscriptionsPtr, int eventsPtr, int count, int storedPtr)
    {
        var memory = caller.GetMemory(GUEST_MEMORY);
        if (memory == null)
            return ERRNO_FAULT;

        if (count <= 0 || count > MAX_SUBSCRIPTIONS)
            return ERRNO_INVAL;

        // Pointers are the guest's: unsigned 32-bit, checked against its memory before use.
        var length = memory.GetLength();
        long subscriptions = (uint)subscriptionsPtr;
        long events = (uint)eventsPtr;
        long stored = (uint)storedPtr;
        if (subscriptions + (long)count * SUBSCRIPTION_SIZE > length
            || events + (long)count * EVENT_SIZE > length
            || stored + sizeof(int) > length)
            return ERRNO_FAULT;

        meter.Bytes(caller, POLL_ONEOFF, (long)count * SUBSCRIPTION_SIZE);

        var descriptors = false;
        var earliest = long.MaxValue;
        for (var i = 0; i < count; i++)
        {
            var subscription = memory.GetSpan(subscriptions + (long)i * SUBSCRIPTION_SIZE, SUBSCRIPTION_SIZE);
            if (subscription[SUBSCRIPTION_KIND_OFFSET] == SUBSCRIPTION_KIND_CLOCK)
                earliest = Math.Min(earliest, clock.Deadline(subscription));
            else
                descriptors = true;
        }

        if (!descriptors)
            clock.AdvanceTo(Math.Max(earliest, clock.After(SHORTEST_WAIT_NS)));

        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var subscription = memory.GetSpan(subscriptions + (long)i * SUBSCRIPTION_SIZE, SUBSCRIPTION_SIZE);
            var kind = subscription[SUBSCRIPTION_KIND_OFFSET];
            var due = kind == SUBSCRIPTION_KIND_CLOCK
                ? !descriptors && clock.Deadline(subscription) <= clock.Now
                : true;
            if (!due)
                continue;

            var userdata = BinaryPrimitives.ReadUInt64LittleEndian(subscription);

            // Error 0, the subscription's kind, nothing counted to read and no flags.
            var item = memory.GetSpan(events + (long)written * EVENT_SIZE, EVENT_SIZE);
            item.Clear();
            BinaryPrimitives.WriteUInt64LittleEndian(item, userdata);
            item[EVENT_KIND_OFFSET] = kind;
            written++;
        }

        BinaryPrimitives.WriteInt32LittleEndian(memory.GetSpan(stored, sizeof(int)), written);
        meter.Bytes(caller, POLL_ONEOFF, (long)written * EVENT_SIZE);
        return ERRNO_SUCCESS;
    }

    #endregion

    #region Nested Types

    /// <summary>The clock of one run: a number the program moves by waiting, never the host's time.</summary>
    private sealed class GuestClock(long start)
    {
        /// <summary>The run's present instant, nanoseconds.</summary>
        public long Now { get; private set; } = start;

        /// <summary>The instant a clock subscription is due at; past the end of time is the end of time.</summary>
        /// <param name="subscription">The subscription record.</param>
        /// <returns>The instant, nanoseconds.</returns>
        public long Deadline(ReadOnlySpan<byte> subscription)
        {
            var timeout = BinaryPrimitives.ReadUInt64LittleEndian(subscription[SUBSCRIPTION_TIMEOUT_OFFSET..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(subscription[SUBSCRIPTION_FLAGS_OFFSET..]);

            var bounded = timeout > long.MaxValue ? long.MaxValue : (long)timeout;
            return (flags & SUBSCRIPTION_FLAG_ABSOLUTE) != 0 ? bounded : After(bounded);
        }

        /// <summary>Moves the clock forward to an instant; it never moves back.</summary>
        /// <param name="instant">The instant, nanoseconds.</param>
        public void AdvanceTo(long instant)
        {
            if (instant > Now)
                Now = instant;
        }

        /// <summary>The instant a duration from now; past the end of time is the end of time.</summary>
        /// <param name="duration">The duration, nanoseconds.</param>
        /// <returns>The instant, nanoseconds.</returns>
        public long After(long duration)
        {
            return duration > long.MaxValue - Now ? long.MaxValue : Now + duration;
        }
    }

    #endregion
}
