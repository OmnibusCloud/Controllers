using System.Runtime.CompilerServices;

namespace OutWit.Controller.AI.Wasm;

/// <summary>
/// The startup guard of the host: the loaded <c>Wasmtime.Dotnet</c> must be the binding this
/// controller was compiled against. The cloud client loads every controller into one shared load
/// context, where an assembly name loads once and the first controller to load it wins; a
/// controller built against another binding would then run on a binding it was never tested with
/// (the engine settings this host makes through the C API reach past the binding, by reflection on
/// its internals). On a mismatch the host is unavailable, with a message naming both versions, and
/// nothing of it starts: the check runs before an engine is made, never inside a run.
/// <para>
/// Every controller that carries the host compiles this file, so the constant below is the one
/// version all of them reference; a test pins it to each controller's <c>Wasmtime</c>
/// PackageReference.
/// </para>
/// </summary>
internal static class WasmHostBinding
{
    #region Constants

    /// <summary>The version of the <c>Wasmtime</c> package (the <c>Wasmtime.Dotnet</c> binding) the controller is built against.</summary>
    public const string BUILT_AGAINST = "48.0.2";

    private const string BINDING_NAME = "Wasmtime.Dotnet";

    private static readonly Lazy<Version?> LOADED = new(LoadedVersion);

    private static readonly Lazy<string?> PROBLEM = new(() => Check(LOADED.Value, BUILT_AGAINST));

    #endregion

    #region Functions

    /// <summary>
    /// Compares a loaded binding's version with the one a controller was built against. The
    /// first three parts must match; a fourth part of 0 is the same as none.
    /// </summary>
    /// <param name="loaded">The loaded binding's assembly version, or null when it could not be loaded.</param>
    /// <param name="builtAgainst">The version the controller was compiled against.</param>
    /// <returns>Null when they match, else the reason the host is unavailable, naming both versions.</returns>
    public static string? Check(Version? loaded, string builtAgainst)
    {
        var expected = Normalize(Version.Parse(builtAgainst));
        if (loaded == null)
            return $"the wasmtime binding ({BINDING_NAME}) could not be loaded; this controller was built against {expected.ToString(3)}";

        if (Normalize(loaded) == expected)
            return null;

        return $"the wasmtime binding loaded in this process is {BINDING_NAME} {loaded}, but this controller was built against {expected.ToString(3)}; " +
               "the sandbox stays off (in a shared load context the first controller to load the binding decides its version)";
    }

    /// <summary>
    /// Refuses to go on when the loaded binding is not the one this controller was built against.
    /// </summary>
    /// <exception cref="InvalidOperationException">The binding is another version, or could not be loaded.</exception>
    public static void ThrowIfUnavailable()
    {
        var problem = Problem;
        if (problem != null)
            throw new InvalidOperationException(problem);
    }

    private static Version Normalize(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    /// <summary>
    /// The loaded binding's version. Kept out of line: a binding that cannot be loaded fails the
    /// type lookup here, inside the guard, and not in the method that asked.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Version? LoadedVersion()
    {
        try
        {
            return BindingAssemblyVersion();
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
        {
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Version? BindingAssemblyVersion()
    {
        return typeof(Wasmtime.Engine).Assembly.GetName().Version;
    }

    #endregion

    #region Properties

    /// <summary>The loaded binding's assembly version, or null when it could not be loaded.</summary>
    public static Version? Loaded => LOADED.Value;

    /// <summary>
    /// Null when the loaded binding is the one this controller was built against; else why the
    /// host is unavailable, naming both versions. Computed once per process.
    /// </summary>
    public static string? Problem => PROBLEM.Value;

    #endregion
}
