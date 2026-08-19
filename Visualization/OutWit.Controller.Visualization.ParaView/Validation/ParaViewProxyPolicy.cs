using OutWit.Controller.Visualization.ParaView.State;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// The version-1 public profile's hard policy, enforced regardless of any allowlist: no user
/// Python executes on a node. Programmable sources/filters/annotations, Python calculators and
/// Python animation cues are rejected by proxy type, and any property known to carry executable
/// code is rejected by name — defense in depth over the allowlist, which is the primary gate.
/// The same lists are handed to the runner for the post-load runtime check.
/// </summary>
public static class ParaViewProxyPolicy
{
    #region Constants

    /// <summary>
    /// Proxy XML types that execute user code, rejected in every group.
    /// </summary>
    public static readonly IReadOnlySet<string> BLOCKED_PROXY_TYPES = new HashSet<string>(StringComparer.Ordinal)
    {
        "ProgrammableSource",
        "ProgrammableFilter",
        "ProgrammableAnnotation",
        "LiveProgrammableSource",
        "PythonCalculator",
        "PythonAnnotation",
        "PythonAnimationCue",
        "PythonScriptView",
        "PythonView"
    };

    /// <summary>
    /// Property names that carry executable code; a proxy exposing one is rejected even when its type is allowlisted.
    /// </summary>
    public static readonly IReadOnlySet<string> BLOCKED_PROPERTY_NAMES = new HashSet<string>(StringComparer.Ordinal)
    {
        "Script",
        "InformationScript",
        "RequestInformationScript",
        "RequestUpdateExtentScript",
        "UpdateExtentScript",
        "PythonPath",
        "PythonScript"
    };

    /// <summary>
    /// XML groups whose proxies read files; their file properties must resolve to package logical paths.
    /// </summary>
    public static readonly IReadOnlyList<string> FILE_REFERENCE_GROUPS = ["sources", "extended_sources", "textures"];

    /// <summary>
    /// Property names that always denote file references, whatever their value looks like.
    /// </summary>
    public static readonly IReadOnlySet<string> FILE_PROPERTY_NAMES = new HashSet<string>(StringComparer.Ordinal)
    {
        "FileName",
        "FileNames",
        "FilePattern",
        "FilePrefix",
        "FileNamePattern",
        "DataFileName",
        "DataDirectory",
        "Directory",
        "XMLFileName",
        "MeshFileName"
    };

    #endregion

    #region Functions

    /// <summary>
    /// Whether a property of a proxy is a file reference under the shared host/runner rule: the proxy
    /// is in a file-reading group and the property is a known file property, ends with
    /// FileName/FileNames, or carries ParaView's files domain. Values are deliberately not inspected —
    /// a Calculator function "p/rho" or an annotation "1/2" is not a path.
    /// </summary>
    /// <param name="proxy">The owning proxy.</param>
    /// <param name="property">The property.</param>
    /// <returns>True when the property's values are file references.</returns>
    public static bool IsFileProperty(ParaViewStateProxy proxy, ParaViewStateProperty property)
    {
        if (!FILE_REFERENCE_GROUPS.Contains(proxy.Group, StringComparer.Ordinal))
            return false;

        return property.HasFileDomain
               || FILE_PROPERTY_NAMES.Contains(property.Name)
               || property.Name.EndsWith("FileName", StringComparison.Ordinal)
               || property.Name.EndsWith("FileNames", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a property carries executable code.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>True when the property must be rejected.</returns>
    public static bool IsBlockedProperty(ParaViewStateProperty property)
    {
        if (!BLOCKED_PROPERTY_NAMES.Contains(property.Name))
            return false;

        // An empty script property is inert (a default-constructed property saved verbatim).
        return property.Values.Any(value => !string.IsNullOrWhiteSpace(value));
    }

    #endregion
}
