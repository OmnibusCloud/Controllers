using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The native wasmtime libraries a controller that carries the host ships, one per platform, and
/// the guard that holds a process to them. The library has no version of its own to ask, so it is
/// known by its SHA-256. A library of another patch computes other fuel and other machine code:
/// wasmtime 48.0.2's lets a guest run past its fuel inside an exception handler
/// (GHSA-m63x-6p34-q65x), and a module compiled by one patch and loaded by another deserialized
/// and then trapped. On a mismatch the host is unavailable, with a message naming the expected and
/// the found hash, the way the binding guard (<see cref="WasmHostBinding"/>) names versions.
/// <para>
/// Every controller that carries the host compiles this file, so the table below is the one set
/// all of them ship; a test pins it to the asset tool's natives pin and to the data assets.
/// </para>
/// </summary>
internal static class WasmHostNatives
{
    #region Constants

    /// <summary>The engine version of the pinned libraries: the newest patch of the binding's long-term line.</summary>
    public const string VERSION = "48.0.8";

    /// <summary>
    /// The SHA-256 of the library each platform's data asset carries, by portable runtime
    /// identifier: wasmtime 48.0.8, taken unchanged from the engine's C API release archives
    /// (the asset tool's <c>toolchain/natives.sh</c>), released as <c>wasmtime-v48.0.8</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> PINNED = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["win-x64"] = "a12576de5f3a2e5616f18beea2a736f829e26315ba48d4fd420057870cd3ee61",   // wasmtime.dll
        ["linux-x64"] = "e637e4f6eccb8d57aca01f5fed8908bd13036ae9270799c1477cb08a83e93de6", // libwasmtime.so
        ["osx-arm64"] = "96a9f7987039b0c667b381c2bff0c891d3650335bce2a06a20ec8c11946fa416"  // libwasmtime.dylib
    };

    private const string LOADED = "loaded in this process";
    private const string CARRIED = "in this controller's folder";

    private static readonly ConcurrentDictionary<string, string> HASHES = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Compares a library's SHA-256 with the one pinned for a platform.
    /// </summary>
    /// <param name="rid">The portable runtime identifier of the platform.</param>
    /// <param name="found">The library's SHA-256, lower-case hex, or null when there is no library.</param>
    /// <param name="where">Where the library is, in words without a machine path ("loaded in this process").</param>
    /// <returns>Null when it is the pinned library, else the reason the host is unavailable, naming both hashes.</returns>
    public static string? Check(string rid, string? found, string where)
    {
        if (!PINNED.TryGetValue(rid, out var expected))
            return $"this controller ships no wasmtime library for {rid} (it ships one for {string.Join(", ", PINNED.Keys)}); the sandbox stays off";

        if (found == null)
            return $"no wasmtime library was found {where}; this controller ships wasmtime {VERSION} for {rid}, SHA-256 {expected}; the sandbox stays off";

        if (string.Equals(found, expected, StringComparison.Ordinal))
            return null;

        return $"the wasmtime library {where} has SHA-256 {found}, but this controller ships wasmtime {VERSION} for {rid}, SHA-256 {expected}; " +
               "the sandbox stays off (in a shared load context the first controller to resolve the library decides it)";
    }

    /// <summary>
    /// Whether a file is the library pinned for this process's platform.
    /// </summary>
    /// <param name="path">The file, or null.</param>
    /// <returns>True when the file exists and hashes to the pin.</returns>
    public static bool IsPinned(string? path)
    {
        return path != null && Check(WasmHostNativeLibrary.PortableRid, Sha256Of(path), CARRIED) == null;
    }

    /// <summary>
    /// The SHA-256 of a file, computed once a path and process (a library is some tens of
    /// megabytes, and the runtime asks a resolver once for every P/Invoke it binds).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>Lower-case hex, or null when the file cannot be read.</returns>
    public static string? Sha256Of(string path)
    {
        if (HASHES.TryGetValue(path, out var known))
            return known;

        try
        {
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            HASHES[path] = hash;
            return hash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Refuses to go on when the library the binding loaded is not the pinned one. Asked after the
    /// binding's first call into the library (the call that loads it) and before the host's own.
    /// </summary>
    /// <exception cref="InvalidOperationException">The library is another one, or none could be told.</exception>
    public static void ThrowIfUnavailable()
    {
        var problem = LoadedProblem();
        if (problem != null)
            throw new InvalidOperationException(problem);
    }

    /// <summary>
    /// Why the library the binding loaded is not the pinned one; null when it is. Every wasmtime
    /// library the operating system has loaded into the process must be the pinned one (a copy of
    /// the host refuses to load any other, but when none answers the runtime's own probing may find
    /// one, in the binding's folder or on the system's search path), and so must the one a copy of
    /// the host noted, or this controller's own file where none did (an isolated context).
    /// </summary>
    /// <returns>The reason, naming both hashes, or null.</returns>
    public static string? LoadedProblem()
    {
        var rid = WasmHostNativeLibrary.PortableRid;
        foreach (var image in LoadedImages())
        {
            var problem = Check(rid, Sha256Of(image), LOADED);
            if (problem != null)
                return problem;
        }

        var path = WasmHostNativeLibrary.LoadedPath;
        return Check(rid, path == null ? null : Sha256Of(path), LOADED);
    }

    /// <summary>
    /// Why the binding found no library at all: this controller's own candidate, refused or not
    /// loadable, or none (another controller's copy of the host may have had one).
    /// </summary>
    /// <param name="failure">The load failure the binding reported.</param>
    /// <returns>The reason, naming the expected hash and the found one.</returns>
    public static string NotLoaded(Exception failure)
    {
        var own = WasmHostNativeLibrary.Locate();
        var problem = Check(WasmHostNativeLibrary.PortableRid, own == null ? null : Sha256Of(own), CARRIED);
        return problem ?? $"the pinned wasmtime library {CARRIED} did not load ({WasmHostNativeLibrary.LoadFailure ?? failure.GetType().Name}); the sandbox stays off";
    }

    /// <summary>
    /// The files of the wasmtime libraries the operating system has loaded into this process, by
    /// the platform's file name; none where the process cannot list its modules.
    /// </summary>
    private static IReadOnlyList<string> LoadedImages()
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.Modules
                .Cast<ProcessModule>()
                .Select(module => module.FileName)
                .Where(path => !string.IsNullOrEmpty(path) && string.Equals(Path.GetFileName(path), WasmHostNativeLibrary.FileName, comparison) && File.Exists(path))
                .Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or NotSupportedException or InvalidOperationException or Win32Exception)
        {
            return [];
        }
    }

    #endregion
}
