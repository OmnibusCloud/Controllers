using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// The version-1 compatibility policy (docs 03, section 7): exact major and minor match between
/// the producing ParaView and the bundled runtime, patch mismatch tolerated; every required plugin
/// must be the bundled OmnibusCloud reader at a version the bundled reader satisfies (same major,
/// greater-or-equal minor); anything else is a permanent compatibility failure reported before any
/// large attachment is downloaded.
/// </summary>
public static class ParaViewCompatibility
{
    #region Functions

    /// <summary>
    /// Checks the package's runtime requirement against the bundled runtime and plugins.
    /// </summary>
    /// <param name="requirement">The package's runtime requirement.</param>
    /// <param name="bundledReaderVersion">Version of the bundled reader, null when none is bundled.</param>
    /// <param name="errors">Receives permanent failures.</param>
    /// <param name="warnings">Receives non-fatal findings.</param>
    public static void Check(
        ParaViewRuntimeRequirementData requirement,
        string? bundledReaderVersion,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        if (requirement.ParaViewMajor <= 0)
            errors.Add("runtime requirement does not name the producing ParaView version");
        else if (requirement.ParaViewMajor != ParaViewRuntimeInfo.RUNTIME_MAJOR || requirement.ParaViewMinor != ParaViewRuntimeInfo.RUNTIME_MINOR)
            errors.Add($"package was produced with ParaView {requirement.ParaViewMajor}.{requirement.ParaViewMinor}; this controller renders with {ParaViewRuntimeInfo.RUNTIME_SERIES} and version 1 requires an exact major.minor match");
        else if (requirement.ParaViewPatch != ParaViewRuntimeInfo.RUNTIME_PATCH)
            warnings.Add($"package was produced with ParaView {requirement.ParaViewMajor}.{requirement.ParaViewMinor}.{requirement.ParaViewPatch}; rendering with {ParaViewRuntimeInfo.RUNTIME_VERSION} (patch mismatch tolerated)");

        foreach (var plugin in requirement.Plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.Name))
            {
                errors.Add("a plugin requirement has no name");
                continue;
            }

            if (!string.Equals(plugin.Name, ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME, StringComparison.Ordinal))
            {
                errors.Add($"required plugin '{plugin.Name}' is not allowlisted; version 1 admits only {ParaViewRuntimeInfo.FRD_READER_PLUGIN_NAME}");
                continue;
            }

            if (bundledReaderVersion == null)
            {
                errors.Add($"required plugin '{plugin.Name}' {plugin.Version}: this controller build bundles no reader");
                continue;
            }

            if (!TryParseVersion(plugin.Version, out var requiredMajor, out var requiredMinor))
            {
                errors.Add($"required plugin '{plugin.Name}' has an unparsable version '{plugin.Version}'");
                continue;
            }

            if (!TryParseVersion(bundledReaderVersion, out var bundledMajor, out var bundledMinor))
            {
                errors.Add($"bundled reader version '{bundledReaderVersion}' is unparsable");
                continue;
            }

            if (bundledMajor != requiredMajor || bundledMinor < requiredMinor)
                errors.Add($"required plugin '{plugin.Name}' {plugin.Version} is not satisfied by the bundled reader {bundledReaderVersion} (same major, greater-or-equal minor required)");
        }
    }

    /// <summary>
    /// Parses a major.minor[.patch] version text.
    /// </summary>
    /// <param name="text">Version text.</param>
    /// <param name="major">Parsed major.</param>
    /// <param name="minor">Parsed minor.</param>
    /// <returns>True when parsable.</returns>
    public static bool TryParseVersion(string? text, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split('.');
        if (parts.Length < 2)
            return false;

        return int.TryParse(parts[0], out major) && int.TryParse(parts[1], out minor) && major >= 0 && minor >= 0;
    }

    #endregion
}
