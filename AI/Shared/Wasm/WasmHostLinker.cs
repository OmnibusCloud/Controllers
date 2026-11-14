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
/// reaches the program, and no wait holds a worker. random_get draws from a PRNG seeded by
/// the run.
///
/// Four calls are taken away, each for a stated reason: renumbering a descriptor, listing a
/// folder, and setting a file's times (by descriptor and by path) answer with an error and
/// do nothing.
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
    private const int SUBSCRIPTION_SIZE = 48;
    private const int SUBSCRIPTION_KIND_OFFSET = 8;
    private const int SUBSCRIPTION_TIMEOUT_OFFSET = 24;
    private const int SUBSCRIPTION_FLAGS_OFFSET = 40;
    private const byte SUBSCRIPTION_KIND_CLOCK = 0;
    private const ushort SUBSCRIPTION_FLAG_ABSOLUTE = 1;
    private const int EVENT_SIZE = 32;
    private const int EVENT_KIND_OFFSET = 10;
    private const int MAX_SUBSCRIPTIONS = 4096;

    #endregion

    #region Functions

    /// <summary>
    /// Creates the linker for one run.
    /// </summary>
    /// <param name="engine">The host's engine.</param>
    /// <param name="randomSeed">The run's seed for random_get.</param>
    /// <returns>A linker with WASI defined and the run's own time and randomness in it; the caller disposes it.</returns>
    public static Linker Create(WasmEngine engine, long randomSeed)
    {
        var linker = new Linker(engine) { AllowShadowing = true };
        linker.DefineWasi();

        // The run's clock, the same for every clock id: it reads the pinned instant until the
        // program waits. Removes the last source of fuel non-determinism found in the runtime
        // spike, and with it any sight of the host's time.
        var clock = new GuestClock(PINNED_CLOCK_NS);
        linker.DefineFunction(WASI_MODULE, "clock_time_get",
            (Caller caller, int clockId, long precision, int timePtr) =>
            {
                caller.GetMemory(GUEST_MEMORY)?.WriteInt64(timePtr, clock.Now);
                return ERRNO_SUCCESS;
            });

        // A wait is a call into the host, where neither fuel nor the wall limit reaches: the
        // real one would hold a worker for as long as the program asked. Here it returns at
        // once and moves the run's clock instead.
        linker.DefineFunction(WASI_MODULE, "poll_oneoff",
            (Caller caller, int subscriptionsPtr, int eventsPtr, int count, int storedPtr) =>
                Poll(caller.GetMemory(GUEST_MEMORY), clock, subscriptionsPtr, eventsPtr, count, storedPtr));

        // Seeded PRNG: deterministic bytes from the run's seed (xorshift64*), so any
        // randomness the guest draws is identical on every node.
        var state = new RandomState(unchecked((ulong)randomSeed) | 1UL);
        linker.DefineFunction(WASI_MODULE, "random_get",
            (Caller caller, int bufferPtr, int length) =>
            {
                var memory = caller.GetMemory(GUEST_MEMORY);
                if (memory == null)
                    return ERRNO_SUCCESS;

                var span = memory.GetSpan(bufferPtr, length);
                for (var i = 0; i < length; i++)
                    span[i] = state.NextByte();

                return ERRNO_SUCCESS;
            });

        // No descriptor juggling. Nothing a guest does needs fd_renumber (the C library's dup2),
        // and it is where wasmtime up to 44.0.2 leaked host descriptors (GHSA-3p27-qvp9-27qf):
        // it answers "not implemented".
        linker.DefineFunction(WASI_MODULE, "fd_renumber", (Caller caller, int from, int to) => ERRNO_NOSYS);

        // No directory listing. The order of a listing is the host file system's (two nodes
        // would print it differently), and before wasmtime 48.0.4 every entry carried three
        // bytes of uninitialized host memory to the guest (GHSA-96f6-r43r-8c24). "Permission
        // denied" is what the interpreters' own importers take for an empty folder.
        linker.DefineFunction(WASI_MODULE, "fd_readdir",
            (Caller caller, int fd, int buffer, int bufferLength, long cookie, int usedPtr) => ERRNO_ACCES);

        // No file times to set: the times of a file are the host's clock, which no run may see
        // or leave behind, and before wasmtime 48.0.3 an out-of-range time panicked the host
        // (GHSA-j2g9-4prp-pf6h).
        linker.DefineFunction(WASI_MODULE, "fd_filestat_set_times",
            (Caller caller, int fd, long accessed, long modified, int flags) => ERRNO_ROFS);
        linker.DefineFunction(WASI_MODULE, "path_filestat_set_times",
            (Caller caller, int fd, int lookupFlags, int pathPtr, int pathLength, long accessed, long modified, int flags) => ERRNO_ROFS);

        return linker;
    }

    /// <summary>
    /// poll_oneoff without waiting. A descriptor is always ready (stdin is a file, stdout and
    /// stderr are files), so when the program asks about any, those are answered and no time
    /// passes. When it asks for timers only, the clock moves to the earliest of them (by a
    /// nanosecond at the least) and that one, with any other due by then, is answered.
    /// </summary>
    private static int Poll(Memory? memory, GuestClock clock, int subscriptionsPtr, int eventsPtr, int count, int storedPtr)
    {
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

    private sealed class RandomState(ulong seed)
    {
        private ulong m_state = seed;

        /// <summary>The next pseudo-random byte of the seeded stream.</summary>
        /// <returns>A byte.</returns>
        public byte NextByte()
        {
            // xorshift64* — deterministic, seed-driven; quality is irrelevant, reproducibility is the point.
            m_state ^= m_state >> 12;
            m_state ^= m_state << 25;
            m_state ^= m_state >> 27;
            return (byte)((m_state * 0x2545F4914F6CDD1DUL) >> 56);
        }
    }

    #endregion
}
