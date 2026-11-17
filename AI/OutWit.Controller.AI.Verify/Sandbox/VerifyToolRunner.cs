using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Runs a tool module the sandbox uses on a task's behalf (the compiler and the linker of the
/// C/C++ runtime) as the host runs any guest: a store of its own, fuel given once the tool is
/// instantiated, an epoch deadline, the memory and table caps, the deterministic clock, folders
/// with tables of names only, and calls into the host that cost fuel a byte. Its stdout is its
/// product (an object, a module), written to a file and capped at the write: a tool cannot fill
/// a disk any more than a program can. Its stderr is the diagnostics a task's author reads, kept
/// in memory up to their cap.
/// </summary>
internal sealed class VerifyToolRunner
{
    #region Fields

    private readonly WasmHost m_host;

    #endregion

    #region Constructors

    /// <summary>
    /// A runner over a host.
    /// </summary>
    /// <param name="host">The host the tool modules are compiled on.</param>
    public VerifyToolRunner(WasmHost host)
    {
        m_host = host;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs the tool once.
    /// </summary>
    /// <param name="module">The tool's compiled module.</param>
    /// <param name="argv">argv, argv[0] included.</param>
    /// <param name="folders">The folders the tool sees, read-only, each with its table of names.</param>
    /// <param name="productPath">The file the tool's stdout goes to.</param>
    /// <param name="productLimitBytes">The most bytes the product may have; the write that passes it ends the run.</param>
    /// <param name="diagnosticsLimitBytes">How much of the tool's stderr is kept.</param>
    /// <param name="captureDirectory">The folder the run's own files live in while it runs.</param>
    /// <param name="fuel">The fuel the run may use.</param>
    /// <param name="wallMs">The wall-clock backstop of the run.</param>
    /// <param name="memoryBytes">The cap of the tool's linear memory.</param>
    /// <returns>How the run ended and what it cost.</returns>
    /// <exception cref="IOException">The product file cannot be written (a fault of the node).</exception>
    public VerifyToolOutcome Run(WasmModule module, IReadOnlyList<string> argv, IReadOnlyList<WasmHostPreopen> folders,
        string productPath, int productLimitBytes, int diagnosticsLimitBytes, string captureDirectory, long fuel, int wallMs, long memoryBytes)
    {
        var outcome = m_host.Run(module, new WasmHostRunRequest
        {
            Argv = argv,
            Preopens = folders,
            CaptureDirectory = captureDirectory,
            Fuel = fuel,
            FuelAfterInstantiation = true,
            MemoryBytes = memoryBytes,
            WallMs = wallMs,
            StdoutFile = productPath,
            StdoutLimitBytes = productLimitBytes,
            StopAtStdoutLimit = true,
            StderrLimitBytes = diagnosticsLimitBytes,
            ChargeHostCalls = true
        });

        // A budget stop reports the whole budget, as a program's does.
        var used = outcome.Verdict == WasmHostVerdict.OutOfFuel ? fuel : outcome.FuelConsumed;
        return new VerifyToolOutcome(outcome.Verdict, outcome.ExitCode, outcome.Trap, used, outcome.WallMs, outcome.Fault == null ? outcome.Stderr : "");
    }

    #endregion
}
