using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// How the conversation tests run a guest: one module on a host, in the envelope a controller
/// gives a run (the fuel counted from the guest's first instruction, so a number is the same on
/// every platform), with a conversation or without one.
/// </summary>
internal static class WasmHostConversationRig
{
    #region Constants

    /// <summary>The fuel of a run that is not about fuel.</summary>
    public const long FUEL = 50_000_000;

    private const long MEMORY_BYTES = 64L << 20;
    private const int WALL_MS = 60_000;

    #endregion

    #region Functions

    /// <summary>
    /// Runs a module once.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="capture">The run's capture folder.</param>
    /// <param name="bytes">The module.</param>
    /// <param name="conversation">The run's conversation (its <c>Next</c> is handed to the run), or null for a run without one.</param>
    /// <param name="charge">Whether the run's calls into the host cost fuel.</param>
    /// <param name="fuel">The run's fuel.</param>
    /// <param name="stdoutLimitBytes">The cap of the run's stdout; 0 for none.</param>
    /// <param name="stopAtStdoutLimit">Whether the write that passes the cap ends the run.</param>
    /// <param name="stdin">A stdin text, or null.</param>
    /// <param name="stdoutFile">A file for the run's stdout, or null.</param>
    /// <param name="census">Where the run's calls are counted, or null.</param>
    /// <param name="preopens">The folders the guest sees, or null for none.</param>
    /// <returns>The run's outcome.</returns>
    public static WasmHostRunOutcome Run(WasmHost host, string capture, byte[] bytes, WasmHostConversationScripted? conversation, bool charge = false,
        long fuel = FUEL, int stdoutLimitBytes = 0, bool stopAtStdoutLimit = false, string? stdin = null, string? stdoutFile = null,
        WasmHostCallCensus? census = null, IReadOnlyList<WasmHostPreopen>? preopens = null)
    {
        using var module = Wasmtime.Module.FromBytes(host.Engine, "main.wasm", bytes);
        return host.Run(module, new WasmHostRunRequest
        {
            Argv = ["main.wasm"],
            Preopens = preopens ?? [],
            Stdin = stdin,
            Conversation = conversation == null ? null : conversation.Next,
            CaptureDirectory = capture,
            Fuel = fuel,
            FuelAfterInstantiation = true,
            MemoryBytes = MEMORY_BYTES,
            WallMs = WALL_MS,
            StdoutLimitBytes = stdoutLimitBytes,
            StopAtStdoutLimit = stopAtStdoutLimit,
            StdoutFile = stdoutFile,
            ChargeHostCalls = charge,
            Census = census
        });
    }

    #endregion
}
