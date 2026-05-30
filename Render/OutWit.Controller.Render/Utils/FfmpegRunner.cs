using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// Cross-platform utility for running ffmpeg in headless mode for RenderVideo.
/// </summary>
public sealed class FfmpegRunner
{
    #region Fields

    private readonly string m_ffmpegPath;
    private readonly string m_ffprobePath;
    private readonly ILogger m_logger;
    private readonly IWitTempStorage m_tempStorage;

    #endregion

    #region Constructors

    public FfmpegRunner(string ffmpegDir, ILogger logger, IWitTempStorage? tempStorage = null)
    {
        m_ffmpegPath = RenderBinaryResolver.ResolveFfmpegPath(ffmpegDir);
        m_ffprobePath = RenderBinaryResolver.ResolveFfprobePath(ffmpegDir);
        m_logger = logger;
        m_tempStorage = tempStorage ?? new WitTempStorageDefault(Path.GetTempPath());

        if (!File.Exists(m_ffmpegPath))
            logger.LogWarning("ffmpeg executable not found at {FfmpegPath}", m_ffmpegPath);
        else
            RenderBinaryResolver.EnsureExecutable(m_ffmpegPath, logger);

        if (!File.Exists(m_ffprobePath))
            logger.LogWarning("ffprobe executable not found at {FfprobePath}", m_ffprobePath);
        else
            RenderBinaryResolver.EnsureExecutable(m_ffprobePath, logger);
    }

    #endregion

    #region Functions

    public async Task EncodeMp4Async(
        string inputPattern,
        string outputFilePath,
        VideoOptionsData options,
        CancellationToken cancellationToken = default)
    {
        var args = BuildEncodeArgs(inputPattern, outputFilePath, options);
        m_logger.LogInformation("ffmpeg encode: {InputPattern} -> {OutputFile}", inputPattern, outputFilePath);
        await RunFfmpegAsync(args, cancellationToken);

        if (!File.Exists(outputFilePath))
            throw new InvalidOperationException($"ffmpeg completed but output file was not found at '{outputFilePath}'.");
    }

    internal async Task StitchTilesAsync(
        IReadOnlyList<RenderTilePlacement> tiles,
        int outputWidth,
        int outputHeight,
        string outputFilePath,
        RenderFormat format,
        CancellationToken cancellationToken = default)
    {
        if (tiles.Count == 0)
            throw new InvalidOperationException("ffmpeg tile stitching requires at least one tile image.");

        var args = BuildStitchArgs(tiles, outputWidth, outputHeight, outputFilePath, format);
        m_logger.LogInformation("ffmpeg tile stitch: {Count} tiles -> {OutputFile}", tiles.Count, outputFilePath);
        await RunFfmpegAsync(args, cancellationToken);

        if (!File.Exists(outputFilePath))
            throw new InvalidOperationException($"ffmpeg completed but stitched output was not found at '{outputFilePath}'.");
    }

    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunProcessAsync(m_ffmpegPath, "-version", cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException("Failed to get ffmpeg version");

        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim() ?? "Unknown";
    }

    internal async Task<string> GetProbeVersionAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunProcessAsync(m_ffprobePath, "-version", cancellationToken);
        if (exitCode != 0)
            throw new InvalidOperationException("Failed to get ffprobe version");

        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim() ?? "Unknown";
    }

    public bool IsAvailable => File.Exists(m_ffmpegPath);

    internal bool IsProbeAvailable => File.Exists(m_ffprobePath);

    internal async Task<RenderImageInfo> GetImageInfoAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        if (!IsProbeAvailable)
            throw new InvalidOperationException($"ffprobe executable not found at '{m_ffprobePath}'.");

        var args = $"-v error -select_streams v:0 -show_entries stream=width,height -show_entries format=format_name -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"";
        var (exitCode, stdout, stderr) = await RunProcessAsync(m_ffprobePath, args, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"ffprobe failed for '{inputPath}' with exit code {exitCode}: {stderr}");
        }

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 3)
            throw new InvalidOperationException($"ffprobe returned an unexpected payload for '{inputPath}': {stdout}");

        if (!int.TryParse(lines[0], out var width) || !int.TryParse(lines[1], out var height))
            throw new InvalidOperationException($"Failed to parse image dimensions from ffprobe output for '{inputPath}': {stdout}");

        return new RenderImageInfo
        {
            Width = width,
            Height = height,
            FormatName = lines[2]
        };
    }

    internal async Task<RenderRawImage> DecodeImageToRgbaAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var imageInfo = await GetImageInfoAsync(inputPath, cancellationToken);
        var rawPath = Path.Combine(m_tempStorage.RootPath, $"witcloud_ffmpeg_decode_{Guid.NewGuid():N}.rgba");

        try
        {
            var args = $"-y -i \"{inputPath}\" -frames:v 1 -f rawvideo -pix_fmt rgba \"{rawPath}\"";
            await RunFfmpegAsync(args, cancellationToken);

            var pixels = await File.ReadAllBytesAsync(rawPath, cancellationToken);
            var expectedLength = imageInfo.Width * imageInfo.Height * 4;
            if (pixels.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    $"Decoded raw image size mismatch for '{inputPath}'. Expected {expectedLength} bytes, got {pixels.Length}.");
            }

            return new RenderRawImage
            {
                Width = imageInfo.Width,
                Height = imageInfo.Height,
                PixelBytes = pixels
            };
        }
        finally
        {
            if (File.Exists(rawPath))
            {
                try { File.Delete(rawPath); }
                catch { }
            }
        }
    }

    internal async Task EncodeRgbaImageAsync(
        RenderRawImage image,
        string outputFilePath,
        RenderFormat format,
        CancellationToken cancellationToken = default)
    {
        var rawPath = Path.Combine(m_tempStorage.RootPath, $"witcloud_ffmpeg_encode_{Guid.NewGuid():N}.rgba");

        try
        {
            await File.WriteAllBytesAsync(rawPath, image.PixelBytes, cancellationToken);

            var outputArgs = format switch
            {
                RenderFormat.PNG => $"-y -f rawvideo -pix_fmt rgba -video_size {image.Width}x{image.Height} -i \"{rawPath}\" -frames:v 1 -update 1 -pix_fmt rgba \"{outputFilePath}\"",
                RenderFormat.JPEG => $"-y -f rawvideo -pix_fmt rgba -video_size {image.Width}x{image.Height} -i \"{rawPath}\" -frames:v 1 -update 1 -q:v 2 \"{outputFilePath}\"",
                _ => throw new InvalidOperationException($"Encoding raw RGBA output does not support format {format}.")
            };

            await RunFfmpegAsync(outputArgs, cancellationToken);

            if (!File.Exists(outputFilePath))
                throw new InvalidOperationException($"ffmpeg completed but encoded image was not found at '{outputFilePath}'.");
        }
        finally
        {
            if (File.Exists(rawPath))
            {
                try { File.Delete(rawPath); }
                catch { }
            }
        }
    }

    internal async Task CropImageAsync(
        string inputPath,
        string outputFilePath,
        int offsetX,
        int offsetY,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"ffmpeg crop requires positive dimensions, got {width}x{height}.");

        if (offsetX < 0 || offsetY < 0)
            throw new InvalidOperationException($"ffmpeg crop requires non-negative offsets, got ({offsetX},{offsetY}).");

        var args = $"-y -i \"{inputPath}\" -vf \"crop={width}:{height}:{offsetX}:{offsetY}\" -frames:v 1 -update 1 \"{outputFilePath}\"";
        m_logger.LogInformation("ffmpeg crop: {InputPath} -> {OutputFilePath} [{Width}x{Height} @ {OffsetX},{OffsetY}]",
            inputPath,
            outputFilePath,
            width,
            height,
            offsetX,
            offsetY);
        await RunFfmpegAsync(args, cancellationToken);

        if (!File.Exists(outputFilePath))
            throw new InvalidOperationException($"ffmpeg completed but cropped output was not found at '{outputFilePath}'.");
    }

    #endregion

    #region Tools

    private static string BuildEncodeArgs(string inputPattern, string outputFilePath, VideoOptionsData options)
    {
        return $"-y -framerate {options.FrameRate} -i \"{inputPattern}\" -c:v libx264 -pix_fmt yuv420p -crf {options.ConstantRateFactor} \"{outputFilePath}\"";
    }

    private static string BuildStitchArgs(
        IReadOnlyList<RenderTilePlacement> tiles,
        int outputWidth,
        int outputHeight,
        string outputFilePath,
        RenderFormat format)
    {
        var inputArgs = string.Join(" ", tiles.Select(me => $"-i \"{me.InputPath}\""));
        var filter = BuildTileOverlayFilter(tiles, outputWidth, outputHeight);
        var codecArgs = format switch
        {
            RenderFormat.PNG => "-frames:v 1 -update 1 -pix_fmt rgba",
            RenderFormat.JPEG => "-frames:v 1 -update 1 -q:v 2",
            _ => throw new InvalidOperationException($"Tile stitching does not support format {format}.")
        };

        return $"-y -f lavfi -i color=color=black@0.0:size={outputWidth}x{outputHeight},format=rgba {inputArgs} -filter_complex \"{filter}\" {codecArgs} \"{outputFilePath}\"";
    }

    private static string BuildTileOverlayFilter(IReadOnlyList<RenderTilePlacement> tiles, int outputWidth, int outputHeight)
    {
        if (tiles.Count == 1)
            return $"[1:v]crop={tiles[0].CropWidth}:{tiles[0].CropHeight}:{tiles[0].CropX}:{tiles[0].CropY}[tile1];[0:v][tile1]overlay={tiles[0].OffsetX}:{tiles[0].OffsetY}";

        var filters = new List<string>();

        for (var index = 0; index < tiles.Count; index++)
        {
            var inputIndex = index + 1;
            filters.Add($"[{inputIndex}:v]crop={tiles[index].CropWidth}:{tiles[index].CropHeight}:{tiles[index].CropX}:{tiles[index].CropY}[tile{inputIndex}]");
        }

        filters.Add($"[0:v][tile1]overlay={tiles[0].OffsetX}:{tiles[0].OffsetY}[tmp1]");

        for (var index = 1; index < tiles.Count; index++)
        {
            var inputIndex = index + 1;
            var outputLabel = index == tiles.Count - 1 ? string.Empty : $"[tmp{index + 1}]";
            filters.Add($"[tmp{index}][tile{inputIndex}]overlay={tiles[index].OffsetX}:{tiles[index].OffsetY}{outputLabel}");
        }

        return string.Join(";", filters);
    }

    private async Task RunFfmpegAsync(string args, CancellationToken cancellationToken)
    {
        var (exitCode, stdout, stderr) = await RunProcessAsync(m_ffmpegPath, args, cancellationToken);
        if (exitCode != 0)
        {
            m_logger.LogError("ffmpeg failed (exit code {ExitCode}):\nstdout: {Stdout}\nstderr: {Stderr}",
                exitCode, stdout, stderr);
            throw new InvalidOperationException($"ffmpeg encode failed with exit code {exitCode}: {stderr}");
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string fileName, string args, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { }
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    #endregion
}
