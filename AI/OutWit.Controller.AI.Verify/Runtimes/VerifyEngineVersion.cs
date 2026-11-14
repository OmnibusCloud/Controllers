using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The engine a node runs, as provenance names it: the .NET binding's version and the version
/// of the native library under it. The native library carries no version of its own, so it is
/// recognised by its SHA-256 among the libraries this controller ships (the data assets, built by
/// the asset tool's natives.sh from the engine's release archives; the wasm host's pin,
/// <see cref="WasmHostNatives.PINNED"/>, whose guard runs nothing on any other); any other library
/// is named "unpinned" with the start of its hash. Computed once per process.
/// </summary>
public static class VerifyEngineVersion
{
    #region Constants

    /// <summary>What a native library that is not one of the shipped ones is called.</summary>
    public const string UNPINNED = "unpinned";

    private const int HASH_PREFIX = 12;

    /// <summary>The native libraries the data assets carry, by SHA-256: wasmtime 48.0.8 for the three platforms (the wasm host's pin).</summary>
    public static readonly IReadOnlyDictionary<string, string> SHIPPED_NATIVES =
        WasmHostNatives.PINNED.Values.ToDictionary(sha256 => sha256, _ => WasmHostNatives.VERSION, StringComparer.Ordinal);

    private static readonly Lazy<string> DESCRIPTION = new(Describe);

    #endregion

    #region Functions

    /// <summary>
    /// Names a native library by its SHA-256.
    /// </summary>
    /// <param name="sha256">The library's SHA-256, lower-case hex, or null when it was not found.</param>
    /// <returns>The version of a shipped library, or "unpinned" with the start of the hash.</returns>
    public static string NativeVersionOf(string? sha256)
    {
        if (string.IsNullOrEmpty(sha256))
            return $"{UNPINNED} (not found)";

        return SHIPPED_NATIVES.TryGetValue(sha256, out var version)
            ? version
            : $"{UNPINNED} {sha256[..Math.Min(HASH_PREFIX, sha256.Length)]}";
    }

    /// <summary>The binding's version and the native library's, e.g. "48.0.2.0 (native 48.0.8)".</summary>
    /// <param name="bindingVersion">The binding's assembly version.</param>
    /// <param name="nativeVersion">What <see cref="NativeVersionOf"/> said of the native library.</param>
    /// <returns>The text provenance and the capabilities document carry.</returns>
    public static string Format(string bindingVersion, string nativeVersion)
    {
        return $"{bindingVersion} (native {nativeVersion})";
    }

    private static string Describe()
    {
        var binding = typeof(Wasmtime.Engine).Assembly.GetName().Version?.ToString() ?? "unknown";

        // The library the binding uses: the one a copy of the wasm host resolved for it (this
        // controller's or another's in a shared context), else the one an isolated context
        // resolves through deps.json from this module's folder.
        var path = WasmHostNativeLibrary.LoadedPath;
        var hash = path == null ? null : WasmHostNatives.Sha256Of(path);

        return Format(binding, NativeVersionOf(hash));
    }

    #endregion

    #region Properties

    /// <summary>The engine of this process, e.g. "48.0.2.0 (native 48.0.8)".</summary>
    public static string Current => DESCRIPTION.Value;

    #endregion
}
