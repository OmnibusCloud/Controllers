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

    /// <summary>Lower-case hex SHA-256 of the .wasm module file.</summary>
    public required string Sha256 { get; init; }

    /// <summary>argv[0] the guest sees (interpreters derive their prefix from it).</summary>
    public required string Argv0 { get; init; }
}
