using Wasmtime;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>How a tool run ended: its exit code (-1 for a trap), the trap if one ended it, and what it cost.</summary>
/// <param name="ExitCode">The exit code; -1 when a trap ended the run.</param>
/// <param name="Trap">The trap that ended the run, or null.</param>
/// <param name="Fuel">The fuel used.</param>
/// <param name="WallMs">The wall time taken.</param>
internal sealed record VerifyToolOutcome(int ExitCode, TrapCode? Trap, long Fuel, int WallMs);
