using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.Output;

/// <summary>
/// Output validation before a result is published (docs 03, section 12): every expected output
/// exists and is a regular file, is non-empty and under the size limit, its signature matches the
/// requested format, its decoded dimensions match the request, it carries an alpha channel when
/// transparency was requested, and the output directory holds exactly the expected set — nothing
/// missing, nothing else.
/// </summary>
public static class ParaViewOutputValidator
{
    #region Functions

    /// <summary>
    /// Validates the rendered output of a single task.
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
        return ValidateSet([outputPath], outputDirectory, format, width, height, transparentBackground)[0];
    }

    /// <summary>
    /// Validates the rendered outputs of a batch: the directory holds exactly the expected files and
    /// every one of them passes the per-file rules.
    /// </summary>
    /// <param name="outputPaths">Expected output paths, in output order.</param>
    /// <param name="outputDirectory">The batch's output directory.</param>
    /// <param name="format">Requested format.</param>
    /// <param name="width">Requested width.</param>
    /// <param name="height">Requested height.</param>
    /// <param name="transparentBackground">Whether transparency was requested.</param>
    /// <returns>The validated image info and byte size of every output, in output order.</returns>
    /// <exception cref="InvalidOperationException">An output is missing, a stray entry exists, or a file violates a rule.</exception>
    public static IReadOnlyList<(ParaViewImageInfo Info, long ByteSize)> ValidateSet(
        IReadOnlyList<string> outputPaths,
        string outputDirectory,
        ParaViewImageFormat format,
        int width,
        int height,
        bool transparentBackground)
    {
        var expected = new HashSet<string>(outputPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var extras = Directory.EnumerateFileSystemEntries(outputDirectory)
            .Where(entry => !expected.Contains(Path.GetFullPath(entry)))
            .Select(Path.GetFileName)
            .ToList();
        if (extras.Count > 0)
            throw new InvalidOperationException($"the output directory holds unexpected entries: {string.Join(", ", extras.Take(8))}");

        var validated = new List<(ParaViewImageInfo Info, long ByteSize)>(outputPaths.Count);
        for (var index = 0; index < outputPaths.Count; index++)
        {
            try
            {
                validated.Add(ValidateFile(outputPaths[index], format, width, height, transparentBackground));
            }
            catch (InvalidOperationException error) when (outputPaths.Count > 1)
            {
                throw new InvalidOperationException($"output {index + 1} of {outputPaths.Count} ({Path.GetFileName(outputPaths[index])}): {error.Message}", error);
            }
        }

        return validated;
    }

    #endregion

    #region Tools

    private static (ParaViewImageInfo Info, long ByteSize) ValidateFile(
        string outputPath,
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
