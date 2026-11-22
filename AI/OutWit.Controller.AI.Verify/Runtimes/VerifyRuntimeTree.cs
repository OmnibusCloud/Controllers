namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// A folder of files a runtime reads besides its module — an interpreter's library that
/// cannot be loaded from one archive — pinned as a whole and verified at resolution like the
/// module: a file changed, added or removed anywhere under it makes the runtime unavailable,
/// so every node runs byte-identical library code.
/// </summary>
/// <param name="Path">The folder, relative to the runtimes root.</param>
/// <param name="Sha256">The tree hash (<see cref="VerifyRuntimeTreeHash"/>), lower-case hex.</param>
public sealed record VerifyRuntimeTree(string Path, string Sha256);
