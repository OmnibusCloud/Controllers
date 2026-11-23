using OutWit.Controller.AI.Smt.Profiles;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// Finds the solver module on a machine: the <see cref="OVERRIDE_VARIABLE"/> environment variable
/// (a file path: tests and non-standard layouts), else <c>wasm/z3-5.4.1.wasm</c> next to the
/// controller assembly. Wherever it is found, its bytes must hash to
/// <see cref="SmtProfile.MODULE_SHA256"/> before they are compiled.
/// </summary>
internal static class SmtModuleLocator
{
    #region Constants

    /// <summary>The environment variable that names the module file instead of the controller's folder.</summary>
    public const string OVERRIDE_VARIABLE = "WITAI_SMT_Z3";

    #endregion

    #region Functions

    /// <summary>The module file of this process.</summary>
    /// <returns>Its full path, or null when there is none.</returns>
    public static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable(OVERRIDE_VARIABLE);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.Exists(overridePath) ? Path.GetFullPath(overridePath) : null;

        var assemblyDirectory = Path.GetDirectoryName(typeof(SmtModuleLocator).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDirectory))
            return null;

        var candidate = Path.Combine(assemblyDirectory, SmtProfile.MODULE_FOLDER, SmtProfile.MODULE_FILE);
        return File.Exists(candidate) ? candidate : null;
    }

    #endregion

    #region Properties

    /// <summary>Where the module is expected, relative to the controller's folder (for messages: no machine path).</summary>
    public static string RelativePath => $"{SmtProfile.MODULE_FOLDER}/{SmtProfile.MODULE_FILE}";

    #endregion
}
