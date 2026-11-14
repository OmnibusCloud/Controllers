using Wasmtime;

namespace OutWit.Controller.AI.Wasm;

/// <summary>What one run of a WASI command module came to.</summary>
/// <param name="Verdict">How the run ended.</param>
/// <param name="ExitCode">The guest's exit code, any status <c>proc_exit</c> was given (WASI's unsigned status read as a signed number: 4294967295 is -1); -1 when it did not end by itself (a trap, a module that never ran, no <c>_start</c>, an engine error).</param>
/// <param name="Trap">The trap that ended the run (budget stops included), or null when none did.</param>
/// <param name="Fault">The host's reason in fixed words when the guest could not say it (a crash trap, a module that never ran, no <c>_start</c>, an engine error), else null; a budget stop has none, so outputs cannot tell fuel from clock.</param>
/// <param name="Stdout">The guest's stdout, capped, as UTF-8.</param>
/// <param name="StdoutTruncated">Whether stdout hit its cap.</param>
/// <param name="Stderr">The guest's stderr, capped, as UTF-8; the fault when the guest wrote nothing and there is one.</param>
/// <param name="StderrTruncated">Whether stderr hit its cap.</param>
/// <param name="FuelConsumed">The fuel the run used: the budget less what was left in the store.</param>
/// <param name="PeakMemoryBytes">The guest's linear memory when the run ended (it only grows, so this is its peak); 0 when no instance was made.</param>
/// <param name="WallMs">The wall time of the guest's part of the run.</param>
/// <param name="EngineError">The engine's exception of an <see cref="WasmHostVerdict.EngineError"/> end, else null.</param>
internal sealed record WasmHostRunOutcome(
    WasmHostVerdict Verdict, int ExitCode, TrapCode? Trap, string? Fault,
    string Stdout, bool StdoutTruncated, string Stderr, bool StderrTruncated,
    long FuelConsumed, long PeakMemoryBytes, int WallMs, WasmtimeException? EngineError = null);
