using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Resolves the wasmtime native library from the module's own <c>runtimes/&lt;rid&gt;/native/</c>
/// folder. The cloud client loads controllers into the shared (default) load context, where a
/// module's <c>deps.json</c> is never consulted and a P/Invoke probes only the application
/// folder and the folder of the calling assembly — neither holds the library. Registered once
/// per process as the <c>Wasmtime.Dotnet</c> assembly's DllImport resolver; under an isolated
/// plugin load context the context resolves the same file first and the resolver is never asked.
/// The resolver is the shared wasm host's (<see cref="WasmHostNativeLibrary"/>, this controller's
/// own compiled-in copy); this class keeps the controller's public face of it.
/// </summary>
public static class VerifyNativeLibrary
{
    #region Constants

    /// <summary>The library name the binding's P/Invokes declare.</summary>
    public const string LIBRARY_NAME = WasmHostNativeLibrary.LIBRARY_NAME;

    #endregion

    #region Functions

    /// <summary>
    /// Registers the resolver for the binding assembly. Safe to call repeatedly and from any
    /// thread; it takes effect only when it runs before the process's first wasmtime P/Invoke.
    /// </summary>
    public static void EnsureRegistered()
    {
        WasmHostNativeLibrary.EnsureRegistered();
    }

    /// <summary>
    /// The library file the resolver would load in this process: the first existing
    /// <c>runtimes/&lt;rid&gt;/native/&lt;file&gt;</c> under the module root, trying the host's
    /// runtime identifier and then the portable one.
    /// </summary>
    /// <returns>The full path, or null when the module carries no library for this platform.</returns>
    public static string? Locate()
    {
        return WasmHostNativeLibrary.Locate();
    }

    /// <summary>
    /// Same as <see cref="Locate()"/> for an explicit module root.
    /// </summary>
    /// <param name="moduleRoot">The folder that holds the module's <c>runtimes/</c> tree.</param>
    /// <returns>The full path, or null when no library exists for this platform under the root.</returns>
    public static string? Locate(string? moduleRoot)
    {
        return WasmHostNativeLibrary.Locate(moduleRoot);
    }

    /// <summary>
    /// The runtime identifiers to try, most specific first, without duplicates: the host's own
    /// identifier (which may be distribution-specific), then the portable one the package ships.
    /// </summary>
    /// <returns>One or two identifiers.</returns>
    public static IReadOnlyList<string> CandidateRids()
    {
        return WasmHostNativeLibrary.CandidateRids();
    }

    #endregion

    #region Properties

    /// <summary>
    /// The path the resolver loaded, or null when it was never asked: the load context resolved
    /// the library itself, or nothing has called into wasmtime yet.
    /// </summary>
    public static string? ResolvedPath => WasmHostNativeLibrary.ResolvedPath;

    /// <summary>The platform's file name for the library.</summary>
    public static string FileName => WasmHostNativeLibrary.FileName;

    /// <summary>The portable runtime identifier: win, linux or osx plus the process architecture.</summary>
    public static string PortableRid => WasmHostNativeLibrary.PortableRid;

    #endregion
}
