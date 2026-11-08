namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// Process-wide sandbox, shared by every Verify node activity so a runtime module is
/// compiled once per process (the expensive step) and reused across batches — the same
/// singleton-adapter caching discipline the other controllers use for heavy state.
/// </summary>
public static class VerifySandboxHost
{
    private static readonly Lazy<VerifyWasmSandbox> s_instance = new(() => new VerifyWasmSandbox());

    /// <summary>The process-wide sandbox, created on first use.</summary>
    public static VerifyWasmSandbox Instance => s_instance.Value;
}
