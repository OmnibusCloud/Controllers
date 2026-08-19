using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Output;

/// <summary>
/// Output validation before a result is published (docs 03, section 12): the output exists and
/// is a regular file, is non-empty and under the size limit, its signature matches the requested
/// format, its decoded dimensions match the request, it carries an alpha channel when transparency
/// was requested, and nothing else sits in the output directory.
/// </summary>
public static class ParaViewOutputValidator
{
    #region Functions

    /// <summary>
    /// Validates the rendered output of a task.
    /// </summary>
    /// <param name="outputPath">Expected output path.</param>
    /// <param name="outputDirectory">The task's output directory (must contain exactly the output).</param>
    /// <param name="format">Requested format.</param>
    /// <param name="width">Requested width.</param>
    /// <param name="height">Requested height.</param>
    /// <param name="transparentBackground">Whether transparency was requested.</param>
    /// <returns>The validated image info and byte size.</returns>
    /// <exception cref="InvalidOperationException">The output is missing or violates a rule.</exception>
    public static (ParaViewImageInfo Info, long ByteSize) Validate(
        string outputPath,
        string outputDirectory,
        ParaViewImageFormat format,
        int width,
        int height,
        bool transparentBackground)
    {
        var info = new FileInfo(outputPath);
        if (!info.Exists)
            throw new InvalidOperationException("the runner exited successfully but produced no output file");

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || (info.Attributes & FileAttributes.Directory) != 0)
            throw new InvalidOperationException("the output path is not a regular file");

        if (info.Length == 0)
            throw new InvalidOperationException("the output file is empty");

        if (info.Length > ParaViewInputLimits.MAX_OUTPUT_BYTES)
            throw new InvalidOperationException($"the output file is {info.Length} bytes, over the {ParaViewInputLimits.MAX_OUTPUT_BYTES} byte limit");

        var extras = Directory.EnumerateFileSystemEntries(outputDirectory)
            .Where(entry => !string.Equals(Path.GetFullPath(entry), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();
        if (extras.Count > 0)
            throw new InvalidOperationException($"the output directory holds unexpected entries: {string.Join(", ", extras.Take(8))}");

        var image = ParaViewImageInfo.TryRead(outputPath)
            ?? throw new InvalidOperationException("the output file is not a recognizable PNG or JPEG image");

        if (image.Format != format)
            throw new InvalidOperationException($"the output signature is {image.Format} but {format} was requested");

        if (image.Width != width || image.Height != height)
            throw new InvalidOperationException($"the output is {image.Width}x{image.Height} but {width}x{height} was requested");

        if (transparentBackground && format == ParaViewImageFormat.Png && !image.HasAlpha)
            throw new InvalidOperationException("a transparent background was requested but the PNG carries no alpha channel");

        return (image, info.Length);
    }

    #endregion
}
