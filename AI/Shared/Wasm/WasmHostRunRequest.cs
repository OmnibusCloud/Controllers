namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// One run of a WASI command module: what the guest sees (argv, environment, folders, stdin) and
/// the envelope it runs in (fuel, memory, wall time, output caps). The guest's clock and
/// randomness are the host's deterministic ones (<see cref="WasmHostLinker"/>), seeded by
/// <see cref="RandomSeed"/>.
/// </summary>
internal sealed class WasmHostRunRequest
{
    #region Properties

    /// <summary>argv, argv[0] included.</summary>
    public required IReadOnlyList<string> Argv { get; init; }

    /// <summary>The guest's environment, in order; nothing of the host's reaches it.</summary>
    public IReadOnlyList<(string Name, string Value)> Environment { get; init; } = [];

    /// <summary>The folders the guest sees, in order (the first is descriptor 3).</summary>
    public IReadOnlyList<WasmHostPreopen> Preopens { get; init; } = [];

    /// <summary>The guest's stdin as text (written as UTF-8), or null for none.</summary>
    public string? Stdin { get; init; }

    /// <summary>
    /// A conversation with the guest over its stdin and stdout, or null (the default: a run
    /// without one is what it always was). With one, the host answers the guest's reads of
    /// descriptor 0 itself (<see cref="WasmHostConversationInput"/>): each time the guest has
    /// consumed everything given so far, the conversation sees what the guest wrote to stdout
    /// since it was last asked and gives the next input; an empty answer is the end of input.
    /// Not together with <see cref="Stdin"/> (a guest has one stdin) nor with
    /// <see cref="StdoutFile"/> (the conversation reads the stdout the host keeps in memory):
    /// <see cref="WasmHost.Run"/> refuses such a request.
    /// </summary>
    public WasmHostConversation? Conversation { get; init; }

    /// <summary>
    /// The host folder the run's stdio files live in while it runs (made when missing; the files
    /// are deleted when the run ends). On a node it is a scope of the client's temp storage.
    /// </summary>
    public required string CaptureDirectory { get; init; }

    /// <summary>The fuel the run may use.</summary>
    public required long Fuel { get; init; }

    /// <summary>
    /// Whether the budget is given again once the module is instantiated, right before
    /// <c>_start</c>, so that it counts the guest's own instructions only. wasmtime charges one
    /// unit of fuel per byte of data segments it copies at instantiation, and only where no
    /// copy-on-write memory image is used (Windows always pays it, Linux does not): a budget set
    /// before instantiation then depends on the platform for a guest that ends near it. With this
    /// on, instantiation runs under the most that copying segments can cost and nothing else
    /// (<see cref="WasmHostLimits.InstantiationFuel"/>), the store is given the budget before
    /// <c>_start</c>, and <see cref="WasmHostRunOutcome.FuelConsumed"/> is the guest's alone.
    /// Off by default: the budget then covers instantiation as well. Verify (since 0.12.0) and
    /// Corpus turn it on.
    /// </summary>
    public bool FuelAfterInstantiation { get; init; }

    /// <summary>The cap of the guest's linear memory, bytes.</summary>
    public required long MemoryBytes { get; init; }

    /// <summary>
    /// The wall time the run may take, milliseconds. A backstop only: it protects the machine from
    /// a run that fuel does not end, and a run it ends (<see cref="WasmHostVerdict.Interrupted"/>)
    /// says something about the machine, not about the program.
    /// </summary>
    public required int WallMs { get; init; }

    /// <summary>
    /// Whether the files the run leaves in <see cref="CaptureDirectory"/> (its stdin and the two
    /// redirect files) are deleted in the background (<see cref="WasmHostDiscard"/>) instead of
    /// on the run's own path. Off by default: the files are gone when the run returns. On, they
    /// are gone soon after, and a machine whose file scanner holds a delete does not pay for it
    /// in every run.
    /// </summary>
    public bool DiscardInBackground { get; init; }

    /// <summary>The seed of the guest's <c>random_get</c> stream.</summary>
    public long RandomSeed { get; init; }

    /// <summary>
    /// The most bytes of stdout the host keeps; 0 for no cap, less than 0 to keep nothing. The cap
    /// is at the write: a byte past it is never stored, in memory or on disk.
    /// </summary>
    public int StdoutLimitBytes { get; init; }

    /// <summary>
    /// The most bytes of stderr the host keeps; 0 for no cap. The cap is at the write; the run
    /// goes on, and what it writes past the cap is dropped.
    /// </summary>
    public int StderrLimitBytes { get; init; }

    /// <summary>
    /// Whether the write that takes stdout past <see cref="StdoutLimitBytes"/> ends the run
    /// (<see cref="WasmHostVerdict.OutputLimit"/>). Off by default: the run goes on and the rest
    /// is dropped.
    /// </summary>
    public bool StopAtStdoutLimit { get; init; }

    /// <summary>
    /// A host file the guest's stdout goes to instead of the outcome (a tool whose stdout is its
    /// product), or null. The cap and the stop rule are the same; the outcome's stdout is empty.
    /// </summary>
    public string? StdoutFile { get; init; }

    /// <summary>
    /// The most bytes the guest may put into its writable folders, all files together, counted at
    /// the write (and at a call that sets a file's size); a write past it answers "no space left".
    /// 0 for no cap.
    /// </summary>
    public long WritableLimitBytes { get; init; }

    /// <summary>
    /// Whether a call into the host costs fuel: a fixed amount every call
    /// (<see cref="WasmHostMeter.CALL_FUEL"/>) and a unit a byte it moves (read, written, filled or
    /// copied, as the engine charges a guest's own bulk copies), so that a run cannot spend the
    /// host's time for nothing. Off by default: only the guest's own instructions cost fuel then.
    /// </summary>
    public bool ChargeHostCalls { get; init; }

    /// <summary>
    /// Where the run's calls into the host are counted by name, or null. A tool for finding where
    /// two machines answer a guest differently; no run needs it.
    /// </summary>
    public WasmHostCallCensus? Census { get; init; }

    /// <summary>
    /// Recognises a refused allocation in the guest's stderr (as capped, or the host's fault when
    /// the guest said nothing), or null when the caller does not tell memory failures apart. Asked
    /// only of a failed end: a nonzero exit, a crash trap, a module without <c>_start</c>.
    /// </summary>
    public Func<string, bool>? OutOfMemory { get; init; }

    #endregion
}
