using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The one table of per-format facts the executor, the workspace and the runner contract share:
/// the wire token the runner receives, the file extension the output is written with, and whether
/// the format can carry transparency.
/// </summary>
public static class ParaViewImageFormats
{
    #region Functions

    /// <summary>
    /// The token the runner receives in the task file.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>png or jpeg.</returns>
    public static string WireToken(ParaViewImageFormat format)
    {
        return format switch
        {
            ParaViewImageFormat.Png => "png",
            ParaViewImageFormat.Jpeg => "jpeg",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown image format")
        };
    }

    /// <summary>
    /// The file extension (without dot) the output is written with.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>png or jpg.</returns>
    public static string Extension(ParaViewImageFormat format)
    {
        return format switch
        {
            ParaViewImageFormat.Png => "png",
            ParaViewImageFormat.Jpeg => "jpg",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unknown image format")
        };
    }

    /// <summary>
    /// Whether the format can carry an alpha channel.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>True for PNG.</returns>
    public static bool SupportsTransparency(ParaViewImageFormat format)
    {
        return format == ParaViewImageFormat.Png;
    }

    #endregion
}
