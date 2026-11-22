using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Runtimes;

/// <summary>
/// Resolves the wasmtime native library from the module's own <c>runtimes/&lt;rid&gt;/native/</c>
/// folder, and only the pinned one. The cloud client loads controllers into the shared (default)
/// load context, where a module's <c>deps.json</c> is never consulted and a P/Invoke probes only
/// the application folder and the folder of the calling assembly — neither holds the library.
/// The binding assembly has one DllImport resolver (the first controller to register wins) and
/// its load context any number of <c>ResolvingUnmanagedDll</c> handlers: this controller's copy
/// of the shared wasm host (<see cref="WasmHostNativeLibrary"/>) registers both, each answering for
/// this module's folder only, so another controller without natives or with other ones cannot take
/// the library away. This class keeps the controller's public face of it.
/// </summary>
public static class VerifyNativeLibrary
{
    #region Constants

    /// <summary>The library name the binding's P/Invokes declare.</summary>
    public const string LIBRARY_NAME = WasmHostNativeLibrary.LIBRARY_NAME;

    #endregion

    #region Functions

    /// <summary>
    /// Registers the resolver for the binding assembly (unless another controller did first) and
    /// the handler of its load context. Safe to call repeatedly and from any thread.
    /// </summary>
    public static void EnsureRegistered()
    {
        WasmHostNativeLibrary.EnsureRegistered();
    }

    /// <summary>
    /// This module's library file: the first existing <c>runtimes/&lt;rid&gt;/native/&lt;file&gt;</c>
    /// under the module's folder, trying the host's runtime identifier and then the portable one.
    /// The resolver loads it only when it is the pinned library.
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
    /// The path this controller's resolver or handler loaded, or null when neither did: the load
    /// context resolved the library itself, another controller's copy of the host answered first,
    /// or nothing has called into wasmtime yet.
    /// </summary>
    public static string? ResolvedPath => WasmHostNativeLibrary.ResolvedPath;

    /// <summary>
    /// The library the binding uses in this process: the one a copy of the wasm host loaded for it
    /// (this controller's or another's), else this module's own file.
    /// </summary>
    public static string? LoadedPath => WasmHostNativeLibrary.LoadedPath;

    /// <summary>Whether another controller's copy of the wasm host loaded the library the binding uses.</summary>
    public static bool LoadedByAnotherController => WasmHostNativeLibrary.LoadedByAnotherCarrier;

    /// <summary>The platform's file name for the library.</summary>
    public static string FileName => WasmHostNativeLibrary.FileName;

    /// <summary>The portable runtime identifier: win, linux or osx plus the process architecture.</summary>
    public static string PortableRid => WasmHostNativeLibrary.PortableRid;

    #endregion
}
