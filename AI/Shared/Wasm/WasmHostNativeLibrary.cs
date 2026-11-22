using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// Resolves the wasmtime native library from the <c>runtimes/&lt;rid&gt;/native/</c> folder of the
/// controller this copy of the host is compiled into, and only when the file there is the pinned
/// library (<see cref="WasmHostNatives"/>). The cloud client loads controllers into the shared
/// (default) load context, where a module's <c>deps.json</c> is never consulted and a P/Invoke
/// probes only the application folder and the folder of the calling assembly — neither holds the
/// library.
/// <para>
/// Every controller that carries the host compiles this file and has its own copy of this class;
/// the binding assembly they share loads once, from the folder of whichever controller needed it
/// first. Each copy answers for its own controller's folder, in two places:
/// </para>
/// <list type="bullet">
/// <item>the binding's DllImport resolver, of which there is one per assembly: the first copy to
/// register it wins, and it answers nothing (<see cref="IntPtr.Zero"/>) when its folder has no
/// pinned library, so the runtime's next steps run;</item>
/// <item>a handler of the binding's load context's <c>ResolvingUnmanagedDll</c> event, which every
/// copy adds (the runtime raises it after the resolver and its own probing found nothing, and takes
/// the first handler that answers).</item>
/// </list>
/// <para>
/// So a controller that ships no natives, or broken ones, cannot take the library away from
/// another: the order the runtime asks in is resolver, load context, default probing, then the
/// handlers. Whichever copy loads the library notes its path process-wide (per binding assembly),
/// where every copy finds the library the binding is using. A library the runtime's own probing
/// finds before any handler is asked (one beside the binding, one on the system's search path) is
/// none of a copy's doing: the natives guard refuses the host then, since it checks every wasmtime
/// library loaded into the process (<see cref="WasmHostNatives.LoadedProblem"/>).
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

    /// <summary>The process-wide note of the library a binding assembly loaded (AppContext data, per binding).</summary>
    private const string LOADED_KEY = "OutWit.Controller.AI.Wasm.NativeLibrary";

    #endregion

    #region Fields

    private static readonly object LOCK = new();
    private static bool m_registered;
    private static string? m_resolvedPath;
    private static string? m_loadFailure;

    #endregion

    #region Functions

    /// <summary>
    /// Registers this copy's resolver for the binding assembly (when no other copy did first) and
    /// its handler of the binding's load context. Safe to call repeatedly and from any thread; it
    /// takes effect for the P/Invokes the runtime binds after it.
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
                // Another controller's copy registered first; it answers for its own folder, and
                // the handler below answers for this one when that finds nothing.
            }

            var context = AssemblyLoadContext.GetLoadContext(BindingAssembly) ?? AssemblyLoadContext.Default;
            context.ResolvingUnmanagedDll += OnResolvingUnmanagedDll;

            m_registered = true;
        }
    }

    /// <summary>
    /// The library file of this controller: the first existing
    /// <c>runtimes/&lt;rid&gt;/native/&lt;file&gt;</c> under its folder, trying the host's runtime
    /// identifier and then the portable one. Whether it is the pinned one is not asked here.
    /// </summary>
    /// <returns>The full path, or null when the controller carries no library for this platform.</returns>
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
        return IsWasmtime(libraryName) ? LoadOwn() : IntPtr.Zero;
    }

    private static IntPtr OnResolvingUnmanagedDll(Assembly assembly, string libraryName)
    {
        return IsWasmtime(libraryName) && assembly == BindingAssembly ? LoadOwn() : IntPtr.Zero;
    }

    /// <summary>
    /// Loads this controller's library when it is the pinned one. Never throws: a library that is
    /// missing, another one or one that does not load is IntPtr.Zero, and the runtime asks on.
    /// </summary>
    private static IntPtr LoadOwn()
    {
        var path = Locate();
        if (path == null || !WasmHostNatives.IsPinned(path))
            return IntPtr.Zero;

        try
        {
            var handle = NativeLibrary.Load(path);
            m_resolvedPath = path;
            Note(path);
            return handle;
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            m_loadFailure = exception.GetType().Name;
            return IntPtr.Zero;
        }
    }

    private static void Note(string path)
    {
        // The first library loaded for this binding stays noted: every later resolution of it
        // gives the same handle.
        lock (LOCK)
        {
            if (AppContext.GetData(LoadedKey) is not string)
                AppContext.SetData(LoadedKey, path);
        }
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
    /// The path this copy loaded through its resolver or its handler, or null when it was never
    /// asked or found nothing to load: the load context resolved the library itself, another
    /// controller's copy answered first, or nothing has called into wasmtime yet.
    /// </summary>
    public static string? ResolvedPath => m_resolvedPath;

    /// <summary>
    /// The library the binding uses in this process: the one a copy of the host (this one or
    /// another controller's) loaded for it, else this controller's own file, which is what an
    /// isolated plugin load context resolves through the module's <c>deps.json</c>.
    /// </summary>
    public static string? LoadedPath => NotedPath ?? Locate();

    /// <summary>The library a copy of the host (this one or another controller's) loaded for the binding, or null when none did.</summary>
    public static string? NotedPath => AppContext.GetData(LoadedKey) as string;

    /// <summary>Whether another controller's copy of the host loaded the library the binding uses.</summary>
    public static bool LoadedByAnotherCarrier => NotedPath is { } noted && !string.Equals(noted, m_resolvedPath, StringComparison.Ordinal);

    /// <summary>The kind of failure of this copy's last attempt to load its pinned library, or null.</summary>
    public static string? LoadFailure => m_loadFailure;

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

    /// <summary>The folder of the controller this copy is compiled into: its <c>runtimes/</c> tree is the one it answers for.</summary>
    public static string? ModuleRoot
    {
        get
        {
            var location = typeof(WasmHostNativeLibrary).Assembly.Location;
            return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
        }
    }

    private static Assembly BindingAssembly => typeof(Wasmtime.Engine).Assembly;

    /// <summary>The AppContext key of the note for this binding assembly (in an isolated context each controller has a binding of its own).</summary>
    private static string LoadedKey => $"{LOADED_KEY}|{BindingAssembly.Location}|{RuntimeHelpers.GetHashCode(BindingAssembly)}";

    #endregion
}
