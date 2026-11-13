namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// One pinned language runtime: a platform-independent WASI command module identified
/// by content hash. Reproducibility and byte-comparison integrity both hang on the
/// pin — a runtime that fails its hash check is treated as absent. The one exception is
/// the task-supplied runtime (<see cref="TaskSupplied"/>): no module of its own, the
/// task carries it, and the hash the provenance records is empty.
/// </summary>
public sealed record VerifyRuntimeDescriptor
{
    /// <summary>Runtime id as tasks reference it (e.g. "python-3.14.6").</summary>
    public required string Id { get; init; }

    /// <summary>Language family: drives argv, environment and the preopen.</summary>
    public required VerifyRuntimeLanguage Language { get; init; }

    /// <summary>
    /// True when the module comes with each task (the raw WASI runtime): nothing on disk to
    /// resolve or pin, <see cref="ModulePath"/> and <see cref="Sha256"/> are empty.
    /// </summary>
    public bool TaskSupplied { get; init; }

    /// <summary>Path of the .wasm command module, relative to the runtimes root.</summary>
    public required string ModulePath { get; init; }

    /// <summary>
    /// Directory preopened read-only as guest "/" (the interpreter's stdlib lives
    /// under it), relative to the runtimes root; null when the module is self-contained.
    /// </summary>
    public string? RootDir { get; init; }

    /// <summary>
    /// True when the guest's "/" is the task's own sources: the sandbox writes them as files
    /// into a fresh folder for the task, preopens it read-only and removes it afterwards, and
    /// argv names the entry file (Lua). Such a runtime has no <see cref="RootDir"/>.
    /// </summary>
    public bool SourcesAsRoot { get; init; }

    /// <summary>Lower-case hex SHA-256 of the .wasm module file.</summary>
    public required string Sha256 { get; init; }

    /// <summary>argv[0] the guest sees (interpreters derive their prefix from it).</summary>
    public required string Argv0 { get; init; }

    /// <summary>
    /// The bytecode layers the runtime loads from its root (relative to the runtimes root),
    /// each hash-pinned and verified at resolution like the module. Empty for a runtime that
    /// is its module alone.
    /// </summary>
    public IReadOnlyList<VerifyRuntimeLayer> Layers { get; init; } = [];

    /// <summary>
    /// The folders of library files the runtime reads (relative to the runtimes root), each
    /// pinned as a whole by a tree hash and verified at resolution. Empty for a runtime whose
    /// library is its module or its layers.
    /// </summary>
    public IReadOnlyList<VerifyRuntimeTree> Trees { get; init; } = [];

    /// <summary>
    /// Guest folders the interpreter searches for libraries besides its own defaults, in
    /// order (Ruby's -I). Empty when the defaults are enough.
    /// </summary>
    public IReadOnlyList<string> LoadPaths { get; init; } = [];
}
