namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The way out of the guest when one of the host's calls ends the run (out of fuel in a call that
/// costs fuel, stdout past its cap); never seen outside a run.
/// </summary>
/// <param name="verdict">How the run ends.</param>
internal sealed class WasmHostRunEnded(WasmHostVerdict verdict) : Exception($"the host ended the run: {verdict}");
