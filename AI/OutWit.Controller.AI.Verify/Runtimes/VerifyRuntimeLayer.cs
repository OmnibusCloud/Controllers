namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// One file a runtime loads besides its module — a bytecode layer such as the standard
/// library zip or the sympy zip — pinned by SHA-256 and verified at resolution exactly like
/// the module, so every node runs byte-identical library code.
/// </summary>
/// <param name="Path">The file, relative to the runtimes root.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the file.</param>
/// <param name="PythonPath">The guest path to put on PYTHONPATH, or null when sys.path finds the layer by itself.</param>
public sealed record VerifyRuntimeLayer(string Path, string Sha256, string? PythonPath);
