using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using System.Reflection;

namespace OutWit.Controller.Render.Tests.Utils;

[TestFixture]
public sealed class FfmpegRunnerTests
{
    #region Constants

    private const string TINY_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAVSURBVBhXY/jPwABC/xkYGhj+gwAARk8JeKKlzvcAAAAASUVORK5CYII=";

    #endregion

    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witcloud_ffmpeg_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
            Directory.Delete(m_testDir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void IsAvailableReturnsFalseForMissingFfmpegTest()
    {
        var runner = new FfmpegRunner(m_testDir, NullLogger.Instance);
        Assert.That(runner.IsAvailable, Is.False);
    }

    [Test]
    public async Task EncodeMp4FromTinyPngSequenceTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var ffmpegDir = Path.Combine(solutionRoot, "@Prerequisites", "ffmpeg");
        var runner = new FfmpegRunner(ffmpegDir, NullLogger.Instance);
        if (!runner.IsAvailable)
            Assert.Ignore($"ffmpeg not found at {ffmpegDir}");

        for (var index = 1; index <= 3; index++)
        {
            File.WriteAllBytes(
                Path.Combine(m_testDir, $"frame_{index:D4}.png"),
                Convert.FromBase64String(TINY_PNG_BASE64));
        }

        var outputPath = Path.Combine(m_testDir, "video.mp4");
        await runner.EncodeVideoAsync(
            Path.Combine(m_testDir, "frame_%04d.png"),
            outputPath,
            new VideoOptionsData { FrameRate = 24, ConstantRateFactor = 23 });

        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));
    }

    // "The result really is in the requested format": every preset is encoded with the real ffmpeg
    // and the OUTPUT is probed — container (format_name), codec (codec_name) and, for ProRes 4444,
    // the alpha-carrying pixel format.
    [TestCase(VideoFormat.Default, "video_default.mp4", "h264", "mp4", null)]
    [TestCase(VideoFormat.Mp4H264, "video_h264.mp4", "h264", "mp4", null)]
    [TestCase(VideoFormat.Mp4H265, "video_h265.mp4", "hevc", "mp4", null)]
    [TestCase(VideoFormat.WebMVp9, "video_vp9.webm", "vp9", "webm", null)]
    // ProRes bit depths are the encoder's own (4444 stores 12-bit); the property that matters is
    // the pixel-format FAMILY — 'yuva' proves the alpha channel survived into the deliverable.
    [TestCase(VideoFormat.MovProres422Hq, "video_prores422hq.mov", "prores", "mov", "yuv422p")]
    [TestCase(VideoFormat.MovProres4444, "video_prores4444.mov", "prores", "mov", "yuva444p")]
    public async Task EncodeVideoSupportsContainerPresetsTest(
        VideoFormat format, string fileName, string expectedCodec, string expectedContainer, string? expectedPixelFormat)
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var ffmpegDir = Path.Combine(solutionRoot, "@Prerequisites", "ffmpeg");
        var runner = new FfmpegRunner(ffmpegDir, NullLogger.Instance);
        if (!runner.IsAvailable || !runner.IsProbeAvailable)
            Assert.Ignore($"ffmpeg/ffprobe not found at {ffmpegDir}");

        // libx265 rejects tiny pictures ("Image size is too small (2x2)") — synthesize 64x64
        // frames through the runner itself instead of the 2x2 test PNG.
        var raw = new RenderRawImage { Width = 64, Height = 64, PixelBytes = new byte[64 * 64 * 4] };
        for (var index = 1; index <= 3; index++)
            await runner.EncodeRgbaImageAsync(raw, Path.Combine(m_testDir, $"frame_{index:D4}.png"), RenderFormat.PNG);

        var outputPath = Path.Combine(m_testDir, fileName);
        await runner.EncodeVideoAsync(
            Path.Combine(m_testDir, "frame_%04d.png"),
            outputPath,
            new VideoOptionsData { FrameRate = 24, ConstantRateFactor = 30, Format = format });

        Assert.That(File.Exists(outputPath), Is.True);
        Assert.That(new FileInfo(outputPath).Length, Is.GreaterThan(0));

        var probe = await ProbeVideoAsync(ffmpegDir, outputPath);
        Assert.That(probe.CodecName, Is.EqualTo(expectedCodec));
        Assert.That(probe.FormatName, Does.Contain(expectedContainer));
        if (expectedPixelFormat is not null)
            Assert.That(probe.PixelFormat, Does.StartWith(expectedPixelFormat));
    }

    private static async Task<(string CodecName, string PixelFormat, string FormatName)> ProbeVideoAsync(
        string ffmpegDir, string filePath)
    {
        var ffprobePath = RenderBinaryResolver.ResolveFfprobePath(ffmpegDir);
        var args = "-v error -select_streams v:0 -show_entries stream=codec_name,pix_fmt " +
                   $"-show_entries format=format_name -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"";

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.EqualTo(0), "ffprobe failed");

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.That(lines, Has.Length.GreaterThanOrEqualTo(3), $"unexpected ffprobe output: {stdout}");
        return (lines[0], lines[1], lines[2]);
    }

    [Test]
    public void GetVideoFileNameMatchesContainerTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FfmpegRunner.GetVideoFileName(VideoFormat.Default), Is.EqualTo("render.mp4"));
            Assert.That(FfmpegRunner.GetVideoFileName(VideoFormat.Mp4H264), Is.EqualTo("render.mp4"));
            Assert.That(FfmpegRunner.GetVideoFileName(VideoFormat.Mp4H265), Is.EqualTo("render.mp4"));
            Assert.That(FfmpegRunner.GetVideoFileName(VideoFormat.WebMVp9), Is.EqualTo("render.webm"));
            Assert.That(FfmpegRunner.GetVideoFileName(VideoFormat.MovProres422Hq), Is.EqualTo("render.mov"));
            Assert.That(FfmpegRunner.GetVideoFileName(VideoFormat.MovProres4444), Is.EqualTo("render.mov"));
        });
    }

    [Test]
    public async Task CropImageProducesExpectedDimensionsTest()
    {
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var ffmpegDir = Path.Combine(solutionRoot, "@Prerequisites", "ffmpeg");
        var runner = new FfmpegRunner(ffmpegDir, NullLogger.Instance);
        if (!runner.IsAvailable)
            Assert.Ignore($"ffmpeg not found at {ffmpegDir}");

        var inputPath = Path.Combine(m_testDir, "source.png");
        File.WriteAllBytes(inputPath, Convert.FromBase64String(TINY_PNG_BASE64));

        var outputPath = Path.Combine(m_testDir, "cropped.png");
        var cropMethod = typeof(FfmpegRunner).GetMethod("CropImageAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                         ?? throw new InvalidOperationException("CropImageAsync method was not found.");
        var cropTask = (Task)cropMethod.Invoke(runner, [inputPath, outputPath, 0, 0, 1, 1, CancellationToken.None])!;
        await cropTask;

        var infoMethod = typeof(FfmpegRunner).GetMethod("GetImageInfoAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                         ?? throw new InvalidOperationException("GetImageInfoAsync method was not found.");
        var infoTask = (Task)infoMethod.Invoke(runner, [outputPath, CancellationToken.None])!;
        await infoTask;

        var resultProperty = infoTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)
                             ?? throw new InvalidOperationException("Result property was not found on the GetImageInfoAsync task.");
        var imageInfo = resultProperty.GetValue(infoTask)
                        ?? throw new InvalidOperationException("GetImageInfoAsync returned a null result.");
        var width = (int)(imageInfo.GetType().GetProperty("Width", BindingFlags.Instance | BindingFlags.Public)?.GetValue(imageInfo)
                          ?? throw new InvalidOperationException("Width property was not found on RenderImageInfo."));
        var height = (int)(imageInfo.GetType().GetProperty("Height", BindingFlags.Instance | BindingFlags.Public)?.GetValue(imageInfo)
                           ?? throw new InvalidOperationException("Height property was not found on RenderImageInfo."));

        Assert.That(width, Is.EqualTo(1));
        Assert.That(height, Is.EqualTo(1));
    }

    #endregion
}
