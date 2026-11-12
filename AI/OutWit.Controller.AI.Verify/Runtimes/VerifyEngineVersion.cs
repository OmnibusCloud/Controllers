using System.Security.Cryptography;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// The engine a node runs, as provenance names it: the .NET binding's version and the version
/// of the native library under it. The native library carries no version of its own, so it is
/// recognised by its SHA-256 among the libraries this controller ships (the data assets, built by
/// the asset tool's natives.sh from the engine's release archives); any other library is named
/// "unpinned" with the start of its hash. Computed once per process.
/// </summary>
public static class VerifyEngineVersion
{
    #region Constants

    /// <summary>What a native library that is not one of the shipped ones is called.</summary>
    public const string UNPINNED = "unpinned";

    private const int HASH_PREFIX = 12;

    /// <summary>The native libraries the data assets carry, by SHA-256: wasmtime 48.0.8 for the three platforms.</summary>
    public static readonly IReadOnlyDictionary<string, string> SHIPPED_NATIVES = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["a12576de5f3a2e5616f18beea2a736f829e26315ba48d4fd420057870cd3ee61"] = "48.0.8", // win-x64/wasmtime.dll
        ["e637e4f6eccb8d57aca01f5fed8908bd13036ae9270799c1477cb08a83e93de6"] = "48.0.8", // linux-x64/libwasmtime.so
        ["96a9f7987039b0c667b381c2bff0c891d3650335bce2a06a20ec8c11946fa416"] = "48.0.8"  // osx-arm64/libwasmtime.dylib
    };

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

        string? hash = null;
        try
        {
            // The file the module-aware resolver loads (or would load, in an isolated context
            // that resolves it through deps.json from the same folder).
            var path = VerifyNativeLibrary.ResolvedPath ?? VerifyNativeLibrary.Locate();
            if (path != null && File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hash = null;
        }

        return Format(binding, NativeVersionOf(hash));
    }

    #endregion

    #region Properties

    /// <summary>The engine of this process, e.g. "48.0.2.0 (native 48.0.8)".</summary>
    public static string Current => DESCRIPTION.Value;

    #endregion
}
