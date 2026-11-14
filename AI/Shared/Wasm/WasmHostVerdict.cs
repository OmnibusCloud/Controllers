namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// How one run of a WASI command module ended, as the host can tell it apart. Every guest-side
/// failure is one of these, never an exception; a controller maps them to its own verdicts.
/// </summary>
internal enum WasmHostVerdict
{
    /// <summary>The guest ended by itself (returned from <c>_start</c> or called <c>proc_exit</c>); the exit code says how, 0 for success.</summary>
    Exited,

    /// <summary>A trap other than a budget stop ended the run (a crash): the trap code names it, the fault holds the fixed words.</summary>
    Trap,

    /// <summary>The fuel ran out: the deterministic budget stop.</summary>
    OutOfFuel,

    /// <summary>The epoch deadline passed: the wall-clock backstop, which depends on the machine.</summary>
    Interrupted,

    /// <summary>The guest failed for want of memory: a failed end (not a success, not a budget stop) whose stderr the caller's test recognises as a refused allocation.</summary>
    OutOfMemory,

    /// <summary>The module could not be instantiated (an import the host does not have, a memory the store cannot grant); it never ran.</summary>
    NotInstantiated,

    /// <summary>The module exports no <c>_start</c>: it is not a command module.</summary>
    NoStart,

    /// <summary>
    /// The engine failed one of the guest's calls without a trap or an exit code: in wasmtime 48, a
    /// <c>proc_exit</c> with a status of 126 or more (WASI preview 1 has none). The engine's own
    /// exception travels with the outcome.
    /// </summary>
    EngineError
}
