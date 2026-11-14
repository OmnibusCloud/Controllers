using System.Reflection;
using System.Runtime.InteropServices;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// Resolves the wasmtime native library from a module's own <c>runtimes/&lt;rid&gt;/native/</c>
/// folder. The cloud client loads controllers into the shared (default) load context, where a
/// module's <c>deps.json</c> is never consulted and a P/Invoke probes only the application
/// folder and the folder of the calling assembly — neither holds the library. Registered once
/// per process as the <c>Wasmtime.Dotnet</c> assembly's DllImport resolver; under an isolated
/// plugin load context the context resolves the same file first and the resolver is never asked.
/// <para>
/// This file is compiled into every controller that carries the sandbox, so each controller has
/// its own copy of this class. The module root is the folder of the binding assembly (in a shared
/// context the binding loads once, and its natives are the ones beside it), else the folder of the
/// controller this copy is compiled into. In a shared context the first copy to register wins; a
/// later copy finds the binding's resolver taken (<see cref="InvalidOperationException"/>) and the
/// first copy's answer stands, which is the same file by the binding pin.
/// </para>
/// </summary>
internal static class WasmHostNativeLibrary
{
    #region Constants

    /// <summary>The library name the binding's P/Invokes declare.</summary>
    public const string LIBRARY_NAME = "wasmtime";

    private const string RUNTIMES_FOLDER = "runtimes";
    private const string NATIVE_FOLDER = "native";
    private const string UNIX_PREFIX = "lib";

    #endregion

    #region Fields

    private static readonly object LOCK = new();
    private static bool m_registered;
    private static string? m_resolvedPath;

    #endregion

    #region Functions

    /// <summary>
    /// Registers the resolver for the binding assembly. Safe to call repeatedly and from any
    /// thread; it takes effect only when it runs before the process's first wasmtime P/Invoke.
    /// </summary>
    public static void EnsureRegistered()
    {
        lock (LOCK)
        {
            if (m_registered)
                return;

            try
            {
                NativeLibrary.SetDllImportResolver(BindingAssembly, Resolve);
            }
            catch (InvalidOperationException)
            {
                // Another component registered a resolver for the binding first; its answer stands.
            }

            m_registered = true;
        }
    }

    /// <summary>
    /// The library file the resolver would load in this process: the first existing
    /// <c>runtimes/&lt;rid&gt;/native/&lt;file&gt;</c> under the module root, trying the host's
    /// runtime identifier and then the portable one.
    /// </summary>
    /// <returns>The full path, or null when the module carries no library for this platform.</returns>
    public static string? Locate()
    {
        return Locate(ModuleRoot);
    }

    /// <summary>
    /// Same as <see cref="Locate()"/> for an explicit module root.
    /// </summary>
    /// <param name="moduleRoot">The folder that holds the module's <c>runtimes/</c> tree.</param>
    /// <returns>The full path, or null when no library exists for this platform under the root.</returns>
    public static string? Locate(string? moduleRoot)
    {
        if (string.IsNullOrEmpty(moduleRoot))
            return null;

        foreach (var rid in CandidateRids())
        {
            var candidate = Path.Combine(moduleRoot, RUNTIMES_FOLDER, rid, NATIVE_FOLDER, FileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// The runtime identifiers to try, most specific first, without duplicates: the host's own
    /// identifier (which may be distribution-specific), then the portable one the package ships.
    /// </summary>
    /// <returns>One or two identifiers.</returns>
    public static IReadOnlyList<string> CandidateRids()
    {
        var rids = new List<string>();
        AddRid(rids, RuntimeInformation.RuntimeIdentifier);
        AddRid(rids, PortableRid);
        return rids;
    }

    private static void AddRid(List<string> rids, string rid)
    {
        if (!string.IsNullOrEmpty(rid) && !rids.Contains(rid))
            rids.Add(rid);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!IsWasmtime(libraryName))
            return IntPtr.Zero;

        var path = Locate();
        if (path == null)
            return IntPtr.Zero;

        // A failure here carries the path in its message, which is the diagnostic that matters;
        // falling back to the default probing would only hide it behind "not found".
        var handle = NativeLibrary.Load(path);
        m_resolvedPath = path;
        return handle;
    }

    private static bool IsWasmtime(string libraryName)
    {
        var name = Path.GetFileNameWithoutExtension(libraryName);
        if (name.StartsWith(UNIX_PREFIX, StringComparison.Ordinal))
            name = name[UNIX_PREFIX.Length..];

        return string.Equals(name, LIBRARY_NAME, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Properties

    /// <summary>
    /// The path this copy's resolver loaded, or null when it was never asked: the load context
    /// resolved the library itself, another controller's copy registered first, or nothing has
    /// called into wasmtime yet.
    /// </summary>
    public static string? ResolvedPath => m_resolvedPath;

    /// <summary>The platform's file name for the library.</summary>
    public static string FileName =>
        OperatingSystem.IsWindows() ? $"{LIBRARY_NAME}.dll"
        : OperatingSystem.IsMacOS() ? $"{UNIX_PREFIX}{LIBRARY_NAME}.dylib"
        : $"{UNIX_PREFIX}{LIBRARY_NAME}.so";

    /// <summary>The portable runtime identifier: win, linux or osx plus the process architecture.</summary>
    public static string PortableRid
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win"
                : OperatingSystem.IsMacOS() ? "osx"
                : "linux";
            return $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        }
    }

    /// <summary>
    /// The folder of the binding assembly (the module root), else of the controller this copy is
    /// compiled into.
    /// </summary>
    public static string? ModuleRoot
    {
        get
        {
            var binding = BindingAssembly.Location;
            var folder = string.IsNullOrEmpty(binding) ? null : Path.GetDirectoryName(binding);
            return folder ?? Path.GetDirectoryName(typeof(WasmHostNativeLibrary).Assembly.Location);
        }
    }

    private static Assembly BindingAssembly => typeof(Wasmtime.Engine).Assembly;

    #endregion
}
