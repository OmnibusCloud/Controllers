using OutWit.Controller.AI.Wasm;
using Wasmtime;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>How a tool run ended and what it cost.</summary>
/// <param name="Verdict">How the host says the run ended.</param>
/// <param name="ExitCode">The exit code; -1 when the tool did not end by itself.</param>
/// <param name="Trap">The trap that ended the run, or null.</param>
/// <param name="Fuel">The fuel used; the whole budget for a run out of fuel.</param>
/// <param name="WallMs">The wall time taken.</param>
/// <param name="Diagnostics">What the tool wrote to stderr, capped.</param>
internal sealed record VerifyToolOutcome(WasmHostVerdict Verdict, int ExitCode, TrapCode? Trap, long Fuel, int WallMs, string Diagnostics);
