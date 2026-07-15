namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// Outcome of one sandboxed execution — the byte-sized output the platform's
/// suitability filter wants. Every resource violation maps to its own verdict so
/// downstream reward pipelines can distinguish "wrong answer" from "ran out of budget".
/// </summary>
public enum VerifyVerdict
{
    /// <summary>Program ran to completion and satisfied its suite (or exited 0 when no suite).</summary>
    Pass = 0,

    /// <summary>Program ran to completion but at least one suite case did not match.</summary>
    Fail = 1,

    /// <summary>Program failed before executing (syntax/parse error — best-effort detection for interpreted runtimes).</summary>
    CompileError = 2,

    /// <summary>Program terminated with a nonzero exit code or an in-guest error.</summary>
    RuntimeError = 3,

    /// <summary>Execution exceeded its CPU (fuel) or wall-clock (epoch) budget.</summary>
    Timeout = 4,

    /// <summary>Execution exceeded its memory cap.</summary>
    MemoryExceeded = 5,

    /// <summary>Program produced more stdout/stderr than the configured cap.</summary>
    OutputExceeded = 6,

    /// <summary>The requested language runtime is not present (or failed its hash pin) on this node.</summary>
    RuntimeUnavailable = 7
}
