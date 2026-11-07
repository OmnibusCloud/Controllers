namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Version-1 input limits of the ParaView controller. Bytes are the primary dimension, counts are
/// secondary guards; every limit is enforced before a large task collection is allocated or a
/// large attachment set is downloaded. Initial values follow the controller assignment and are to
/// be finalized from benchmark data (service-tier configuration is a later concern).
/// </summary>
public static class ParaViewInputLimits
{
    #region Constants

    /// <summary>Maximum declared bytes of the whole package (state + every attachment).</summary>
    public const long MAX_PACKAGE_BYTES = 64L * 1024 * 1024 * 1024;

    /// <summary>Maximum declared bytes one task's subset (state + its attachments) may materialize on a node.</summary>
    public const long MAX_TASK_SUBSET_BYTES = 16L * 1024 * 1024 * 1024;

    /// <summary>Maximum byte size of the state file.</summary>
    public const long MAX_STATE_BYTES = 32L * 1024 * 1024;

    /// <summary>Maximum byte size of the opaque package manifest carried for provenance.</summary>
    public const int MAX_MANIFEST_CHARS = 4 * 1024 * 1024;

    /// <summary>Maximum number of attachments in a package.</summary>
    public const int MAX_ATTACHMENTS = 10_000;

    /// <summary>Maximum number of independent outputs (tasks) per job.</summary>
    public const int MAX_OUTPUTS = 10_000;

    /// <summary>Maximum output width or height in pixels.</summary>
    public const int MAX_DIMENSION = 16_384;

    /// <summary>Maximum output pixels per frame (128 megapixels).</summary>
    public const long MAX_PIXELS = 128L * 1024 * 1024;

    /// <summary>Maximum byte size of one rendered output.</summary>
    public const long MAX_OUTPUT_BYTES = 1L * 1024 * 1024 * 1024;

    /// <summary>Maximum XML nesting depth of the state.</summary>
    public const int MAX_XML_DEPTH = 64;

    /// <summary>Maximum number of XML elements in the state.</summary>
    public const int MAX_XML_ELEMENTS = 2_000_000;

    /// <summary>Maximum number of XML attributes in the state.</summary>
    public const int MAX_XML_ATTRIBUTES = 8_000_000;

    /// <summary>Maximum characters of a single attribute value or text node in the state.</summary>
    public const int MAX_XML_TEXT_CHARS = 1_000_000;

    /// <summary>Maximum length of a logical path.</summary>
    public const int MAX_LOGICAL_PATH_CHARS = 1024;

    /// <summary>Maximum length of a view registration name.</summary>
    public const int MAX_VIEW_ID_CHARS = 256;

    /// <summary>Maximum characters of process output retained per task (stdout and stderr each).</summary>
    public const int MAX_PROCESS_OUTPUT_CHARS = 64 * 1024;

    /// <summary>Maximum characters of diagnostics carried in a result.</summary>
    public const int MAX_DIAGNOSTICS_CHARS = 8 * 1024;

    /// <summary>Wall-clock limit of one task's pvpython run.</summary>
    public static readonly TimeSpan TASK_WALL_CLOCK_LIMIT = TimeSpan.FromHours(2);

    #endregion

    #region Functions

    /// <summary>
    /// Validates output dimensions against the per-dimension and per-frame pixel limits.
    /// </summary>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    /// <returns>Null when valid, otherwise the violation.</returns>
    public static string? CheckDimensions(int width, int height)
    {
        if (width < 1 || height < 1)
            return $"output dimensions must be positive, got {width}x{height}";

        if (width > MAX_DIMENSION || height > MAX_DIMENSION)
            return $"output dimensions {width}x{height} exceed the {MAX_DIMENSION} pixels per dimension limit";

        if ((long)width * height > MAX_PIXELS)
            return $"output {width}x{height} exceeds the {MAX_PIXELS / (1024 * 1024)} megapixel per frame limit";

        return null;
    }

    #endregion
}
