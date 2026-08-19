namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The pinned ParaView runtime this controller version renders with, and the plugins it bundles.
/// Kept in one place so the asset declarations, the allowlist, the compatibility check and the
/// result provenance cannot drift apart.
/// </summary>
public static class ParaViewRuntimeInfo
{
    #region Constants

    /// <summary>Major version of the bundled ParaView runtime.</summary>
    public const int RUNTIME_MAJOR = 6;

    /// <summary>Minor version of the bundled ParaView runtime.</summary>
    public const int RUNTIME_MINOR = 1;

    /// <summary>Patch version of the bundled ParaView runtime.</summary>
    public const int RUNTIME_PATCH = 1;

    /// <summary>The bundled runtime as major.minor (the allowlist key), derived from the numbers above.</summary>
    public static readonly string RUNTIME_SERIES = $"{RUNTIME_MAJOR}.{RUNTIME_MINOR}";

    /// <summary>The bundled runtime as major.minor.patch, derived from the numbers above.</summary>
    public static readonly string RUNTIME_VERSION = $"{RUNTIME_MAJOR}.{RUNTIME_MINOR}.{RUNTIME_PATCH}";

    /// <summary>Registered name of the bundled OmnibusCloud .frd reader plugin.</summary>
    public const string FRD_READER_PLUGIN_NAME = "OmnibusCloudFrdReader";

    /// <summary>Embedded resource name of the bundled reader (absent until the reader milestone ships it).</summary>
    public const string FRD_READER_RESOURCE = "plugins/omnibuscloud_frd_reader.py";

    /// <summary>Embedded resource name of the controller-owned task runner.</summary>
    public const string RUNNER_RESOURCE = "runner/render_task.py";

    /// <summary>File name of the task runner inside a task's work directory.</summary>
    public const string RUNNER_FILE_NAME = "render_task.py";

    /// <summary>File name of the bundled reader inside a task's plugin directory.</summary>
    public const string FRD_READER_FILE_NAME = "omnibuscloud_frd_reader.py";

    #endregion

    #region Fields

    private static readonly Lazy<string?> BUNDLED_READER_VERSION = new(ReadBundledReaderVersion);

    #endregion

    #region Functions

    /// <summary>
    /// Version of the bundled reader as declared by its <c>__version__</c> marker, or null when the
    /// controller carries no reader. Read once per process.
    /// </summary>
    /// <returns>The reader version text or null.</returns>
    public static string? BundledReaderVersion()
    {
        return BUNDLED_READER_VERSION.Value;
    }

    /// <summary>
    /// Whether a runtime version text (for example "6.1.1" or "6.1.1-fake") belongs to a major.minor series.
    /// </summary>
    /// <param name="versionText">Version text as reported by the runtime.</param>
    /// <param name="series">major.minor.</param>
    /// <returns>True when the first two components match.</returns>
    public static bool IsSameSeries(string? versionText, string series)
    {
        if (string.IsNullOrWhiteSpace(versionText))
            return false;

        var parts = versionText.Trim().Split('.', '-', '+', ' ');
        return parts.Length >= 2 && string.Equals($"{parts[0]}.{parts[1]}", series, StringComparison.Ordinal);
    }

    private static string? ReadBundledReaderVersion()
    {
        var text = ReadEmbeddedText(FRD_READER_RESOURCE);
        if (text == null)
            return null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("__version__", StringComparison.Ordinal))
                continue;

            var separator = line.IndexOf('=');
            if (separator < 0)
                continue;

            return line[(separator + 1)..].Trim().Trim('"', '\'', ' ', '\r');
        }

        return null;
    }

    /// <summary>
    /// Reads an embedded text resource of the controller assembly.
    /// </summary>
    /// <param name="resourceName">Logical resource name.</param>
    /// <returns>The text, or null when the resource is absent.</returns>
    public static string? ReadEmbeddedText(string resourceName)
    {
        using var stream = typeof(ParaViewRuntimeInfo).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    #endregion
}
