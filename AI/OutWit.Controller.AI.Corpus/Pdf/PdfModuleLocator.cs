namespace OutWit.Controller.AI.Corpus.Pdf;

/// <summary>
/// Finds the extractor module on a machine: the <see cref="OVERRIDE_VARIABLE"/> environment
/// variable (a file path: tests and non-standard layouts), else <c>wasm/pdftext.wasm</c> next to
/// the controller assembly, where the module's data asset extracts it. Wherever it is found, its
/// bytes must hash to <see cref="PdfStageLimits.MODULE_SHA256"/> before they run.
/// </summary>
public static class PdfModuleLocator
{
    #region Constants

    /// <summary>The environment variable that names the module file instead of the controller's folder.</summary>
    public const string OVERRIDE_VARIABLE = "WITAI_CORPUS_PDFTEXT";

    #endregion

    #region Functions

    /// <summary>The module file of this process.</summary>
    /// <returns>Its full path, or null when there is none.</returns>
    public static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable(OVERRIDE_VARIABLE);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return File.Exists(overridePath) ? Path.GetFullPath(overridePath) : null;

        var assemblyDirectory = Path.GetDirectoryName(typeof(PdfModuleLocator).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyDirectory))
            return null;

        var candidate = Path.Combine(assemblyDirectory, PdfStageLimits.MODULE_FOLDER, PdfStageLimits.MODULE_FILE);
        return File.Exists(candidate) ? candidate : null;
    }

    #endregion

    #region Properties

    /// <summary>Where the module is expected, relative to the controller's folder (for messages: no machine path).</summary>
    public static string RelativePath => $"{PdfStageLimits.MODULE_FOLDER}/{PdfStageLimits.MODULE_FILE}";

    #endregion
}
