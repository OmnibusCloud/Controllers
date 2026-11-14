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
    /// on, instantiation runs under the budget plus the most that copying segments can cost
    /// (<see cref="WasmHostLimits.InstantiationFuel"/>), the store is filled up to the budget again
    /// before <c>_start</c>, and <see cref="WasmHostRunOutcome.FuelConsumed"/> is the guest's alone.
    /// Off by default: the budget then covers instantiation as well (Verify's behaviour).
    /// </summary>
    public bool FuelAfterInstantiation { get; init; }

    /// <summary>The cap of the guest's linear memory, bytes.</summary>
    public required long MemoryBytes { get; init; }

    /// <summary>The wall time the run may take, milliseconds; a backstop, fuel binds first.</summary>
    public required int WallMs { get; init; }

    /// <summary>The seed of the guest's <c>random_get</c> stream.</summary>
    public long RandomSeed { get; init; }

    /// <summary>The most bytes of stdout the outcome carries; 0 for no cap.</summary>
    public int StdoutLimitBytes { get; init; }

    /// <summary>The most bytes of stderr the outcome carries; 0 for no cap.</summary>
    public int StderrLimitBytes { get; init; }

    /// <summary>
    /// Recognises a refused allocation in the guest's stderr (as capped, or the host's fault when
    /// the guest said nothing), or null when the caller does not tell memory failures apart. Asked
    /// only of a failed end: a nonzero exit, a crash trap, a module without <c>_start</c>.
    /// </summary>
    public Func<string, bool>? OutOfMemory { get; init; }

    #endregion
}
