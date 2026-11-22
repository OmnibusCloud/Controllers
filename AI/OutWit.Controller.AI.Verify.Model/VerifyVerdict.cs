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

    /// <summary>
    /// Execution used up its fuel budget. Fuel counts the program's own work and is the same on
    /// every node; the wall clock ends no run with a verdict (a run the clock ended is a fault of
    /// the node, <see cref="NoResult"/> if no node does better).
    /// </summary>
    Timeout = 4,

    /// <summary>Execution exceeded its memory cap.</summary>
    MemoryExceeded = 5,

    /// <summary>Program wrote more to stdout than its cap; the run ended at the write that passed it.</summary>
    OutputExceeded = 6,

    /// <summary>
    /// Not produced since 0.13.0: a node that lacks a runtime (or whose copy fails its hash pin)
    /// refuses the whole package and the work goes elsewhere, and an unknown runtime id is refused
    /// when the taskset is split. Kept so that documents of earlier runs can be read.
    /// </summary>
    RuntimeUnavailable = 7,

    /// <summary>
    /// No verdict: the nodes that were given the task could not run it to an end of its own (a
    /// fault of theirs, the wall-clock backstop included), also when it was sent out once more.
    /// The documents carry a reason code (<see cref="VerifyFault"/>). It says nothing about the program.
    /// </summary>
    NoResult = 8,

    /// <summary>
    /// The executions of the task disagreed, and three more on different machines did not give
    /// one answer from at least two of them: the task is out of the pass counts and of the products.
    /// </summary>
    Disputed = 9
}
