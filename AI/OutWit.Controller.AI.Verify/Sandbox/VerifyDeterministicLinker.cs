using Wasmtime;
using WasmEngine = Wasmtime.Engine;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// A WASI linker whose clock and randomness are replaced with deterministic implementations:
/// every clock_time_get returns the same instant and random_get draws from a PRNG seeded by
/// the task, so a program's execution — output and fuel — is reproducible on any node. The
/// integrity foundation the controller compares against.
/// </summary>
internal static class VerifyDeterministicLinker
{
    #region Constants

    /// <summary>Fixed value returned by the pinned clock (nanoseconds); constant → deterministic.</summary>
    public const long PINNED_CLOCK_NS = 0;

    private const string WASI_MODULE = "wasi_snapshot_preview1";
    private const string GUEST_MEMORY = "memory";
    private const int ERRNO_SUCCESS = 0;

    #endregion

    #region Functions

    /// <summary>
    /// Creates the linker for one run.
    /// </summary>
    /// <param name="engine">The sandbox's engine.</param>
    /// <param name="randomSeed">The task's seed for random_get.</param>
    /// <returns>A linker with WASI defined and the two sources shadowed; the caller disposes it.</returns>
    public static Linker Create(WasmEngine engine, long randomSeed)
    {
        var linker = new Linker(engine) { AllowShadowing = true };
        linker.DefineWasi();

        // Fixed clock: every clock_time_get returns the same instant. Removes the last
        // source of fuel non-determinism found in the runtime spike.
        linker.DefineFunction(WASI_MODULE, "clock_time_get",
            (Caller caller, int clockId, long precision, int timePtr) =>
            {
                caller.GetMemory(GUEST_MEMORY)?.WriteInt64(timePtr, PINNED_CLOCK_NS);
                return ERRNO_SUCCESS;
            });

        // Seeded PRNG: deterministic bytes from the task seed (xorshift64*), so any
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

        return linker;
    }

    #endregion

    #region Nested Types

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
