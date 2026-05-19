using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

/// <summary>
/// Cube-diorama [Explicit] diagnostic tests that exercise the bundled
/// Render.* scripts against the @Data/cube_diorama scene. All three are
/// [Explicit] (skipped by default) and were originally co-located in the
/// production-script mega-fixture; isolating them keeps a 475-line
/// intermediate-diagnostics test from drowning the rest.
/// </summary>
[TestFixture]
internal sealed class RenderProductionScriptCubeDioramaTests : RenderProductionScriptBlenderTestsBase
{
    #region Tests

    [Test]
    [Explicit("Real Blender end-to-end tiled still render for cube_diorama using the Blender addon default tiled settings (2x2, overlap 8).")]
    public async Task BundledRenderStillTiledCubeDioramaDefaultSettingsRealRunProducesNonBlackImageTest()
    {
        RequireCubeDiorama();
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStillTiled.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_cubeDioramaBlendPath!);
        var options = await ReadSceneRenderOptionsAsync(m_cubeDioramaBlendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            options,
            CreateTileOptions(8, TileBlendMode.CenterPriorityCrop));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        AssertImageIsNotSolidBlack(storedPath, "Bundled RenderStillTiled cube_diorama default tiled settings");
    }

    [Test]
    [Explicit("Real Blender local compare test for cube_diorama regular still vs tiled still using addon-default tiled settings.")]
    public async Task BundledRenderStillAndRenderStillTiledCubeDioramaLocalRunProduceSameDimensionsTest()
    {
        RequireCubeDiorama();
        var stillScript = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStill.wit"));
        var tiledScript = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStillTiled.wit"));
        var options = await ReadSceneRenderOptionsAsync(m_cubeDioramaBlendPath!);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_cubeDioramaBlendPath!);

        var stillJob = m_engine.Compile(stillScript);
        var stillStatus = await m_engine.ScheduleAndWaitAsync(
            stillJob,
            CreateSceneRef(sceneBlobId),
            1,
            options);

        Assert.That(stillStatus.Result, Is.EqualTo(WitProcessingResult.Completed), $"Regular still job failed: {stillStatus.Message}");

        var stillBlobId = (Guid?)stillJob.Variables["result"].Value;
        Assert.That(stillBlobId, Is.Not.Null);

        var tiledJob = m_engine.Compile(tiledScript);
        var tiledStatus = await m_engine.ScheduleAndWaitAsync(
            tiledJob,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            options,
            CreateTileOptions(8, TileBlendMode.CenterPriorityCrop));

        Assert.That(tiledStatus.Result, Is.EqualTo(WitProcessingResult.Completed), $"Tiled still job failed: {tiledStatus.Message}");

        var tiledBlobId = (Guid?)tiledJob.Variables["result"].Value;
        Assert.That(tiledBlobId, Is.Not.Null);

        var stillPath = m_blobService.GetStoredPath(stillBlobId!.Value);
        var tiledPath = m_blobService.GetStoredPath(tiledBlobId!.Value);

        using var stillImage = Image.Load<Rgba32>(stillPath);
        using var tiledImage = Image.Load<Rgba32>(tiledPath);
        var meanDiff = CalculateMeanAbsoluteRgbDifference(stillImage, tiledImage);
        var compareReportPath = Path.Combine(m_blobStoragePath, "cube_diorama_local_compare.txt");
        await File.WriteAllTextAsync(
            compareReportPath,
            $"Regular path: {stillPath}{Environment.NewLine}" +
            $"Tiled path: {tiledPath}{Environment.NewLine}" +
            $"Regular dimensions: {stillImage.Width}x{stillImage.Height}{Environment.NewLine}" +
            $"Tiled dimensions: {tiledImage.Width}x{tiledImage.Height}{Environment.NewLine}" +
            $"Mean absolute RGB difference: {meanDiff:F4}{Environment.NewLine}");

        TestContext.Progress.WriteLine($"Local cube_diorama compare regular dimensions: {stillImage.Width}x{stillImage.Height}");
        TestContext.Progress.WriteLine($"Local cube_diorama compare tiled dimensions: {tiledImage.Width}x{tiledImage.Height}");
        TestContext.Progress.WriteLine($"Local cube_diorama compare regular path: {stillPath}");
        TestContext.Progress.WriteLine($"Local cube_diorama compare tiled path: {tiledPath}");
        TestContext.Progress.WriteLine($"Local cube_diorama compare mean absolute RGB difference: {meanDiff:F4}");
        TestContext.Progress.WriteLine($"Local cube_diorama compare report: {compareReportPath}");

        Assert.That(tiledImage.Width, Is.EqualTo(stillImage.Width));
        Assert.That(tiledImage.Height, Is.EqualTo(stillImage.Height));
    }

    [Test]
    [Explicit("Real Blender local diagnostics for cube_diorama tiled still intermediate tasks, rendered tiles, and final stitched output.")]
    public async Task RenderStillTiledCubeDioramaLocalIntermediateDiagnosticsTest()
    {
        RequireCubeDiorama();
        var script = """
                     Job:TiledDiag(Blob:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderTaskCollection:tasks = Render.SplitTiles(scene, frame, tilesX, tilesY, options, tileOptions);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectTiles(rendered, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_cubeDioramaBlendPath!);
        var options = await ReadSceneRenderOptionsAsync(m_cubeDioramaBlendPath!);
        var tileOptions = CreateTileOptions(8, TileBlendMode.CenterPriorityCrop);
        var outputDirectory = Path.Combine(
            m_solutionRoot!,
            "@Output",
            "LiveTestOutputs",
            "LocalRenderStillTiledCubeDioramaDiagnostics",
            $"{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            options,
            tileOptions);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var tasks = job.Variables["tasks"].Value as IReadOnlyList<RenderTaskData?>;
        var rendered = job.Variables["rendered"].Value as IReadOnlyList<RenderResultData?>;
        var resultBlobId = (Guid?)job.Variables["result"].Value;

        Assert.That(tasks, Is.Not.Null);
        Assert.That(rendered, Is.Not.Null);
        Assert.That(resultBlobId, Is.Not.Null);

        var diagnostics = new StringBuilder();
        diagnostics.AppendLine($"Scene: {m_cubeDioramaBlendPath}");
        diagnostics.AppendLine($"Options: {options.ResolutionX}x{options.ResolutionY}; Samples={options.Samples}; Format={options.Format}");
        diagnostics.AppendLine($"Tile options: OverlapPx={tileOptions.OverlapPx}; BlendMode={tileOptions.BlendMode}");
        diagnostics.AppendLine($"Status: {status.Result}; Message={status.Message}");
        diagnostics.AppendLine();

        var referenceBase = Path.Combine(outputDirectory, "reference_full_");
        var referencePath = await RenderFrameDirectAsync(m_cubeDioramaBlendPath!, 1, referenceBase, options);
        diagnostics.AppendLine($"Reference full render: {referencePath}");
        diagnostics.AppendLine();

        using var referenceImage = Image.Load<Rgba32>(referencePath);

        for (var index = 0; index < tasks!.Count; index++)
        {
            var task = tasks[index];
            var result = rendered![index];
            diagnostics.AppendLine($"Task {index}:");

            if (task != null)
            {
                diagnostics.AppendLine($"  Tile bounds: X[{task.TileMinX:F4},{task.TileMaxX:F4}] Y[{task.TileMinY:F4},{task.TileMaxY:F4}]");
                diagnostics.AppendLine($"  Render bounds: X[{task.RenderMinX:F4},{task.RenderMaxX:F4}] Y[{task.RenderMinY:F4},{task.RenderMaxY:F4}]");
            }

            if (result != null)
            {
                var tilePath = m_blobService.GetStoredPath(result.ImageBlobId);
                using var tileImage = Image.Load<Rgba32>(tilePath);
                var copiedTilePath = Path.Combine(outputDirectory, $"tile_{index:D2}.png");
                File.Copy(tilePath, copiedTilePath, overwrite: true);
                var expectedCropPath = Path.Combine(outputDirectory, $"tile_{index:D2}_expected.png");
                var cropX = (int)Math.Round(result.EffectiveRenderMinX * referenceImage.Width, MidpointRounding.AwayFromZero);
                var cropY = (int)Math.Round((1f - result.EffectiveRenderMaxY) * referenceImage.Height, MidpointRounding.AwayFromZero);
                var cropWidth = (int)Math.Round((result.EffectiveRenderMaxX - result.EffectiveRenderMinX) * referenceImage.Width, MidpointRounding.AwayFromZero);
                var cropHeight = (int)Math.Round((result.EffectiveRenderMaxY - result.EffectiveRenderMinY) * referenceImage.Height, MidpointRounding.AwayFromZero);
                using var expectedCrop = referenceImage.Clone(me => me.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight)));
                await expectedCrop.SaveAsPngAsync(expectedCropPath);
                var meanDiff = CalculateMeanAbsoluteRgbDifference(tileImage, expectedCrop);
                diagnostics.AppendLine($"  Result bounds: X[{result.TileMinX:F4},{result.TileMaxX:F4}] Y[{result.TileMinY:F4},{result.TileMaxY:F4}]");
                diagnostics.AppendLine($"  Result render bounds: X[{result.RenderMinX:F4},{result.RenderMaxX:F4}] Y[{result.RenderMinY:F4},{result.RenderMaxY:F4}]");
                diagnostics.AppendLine($"  Image: {tileImage.Width}x{tileImage.Height} -> {copiedTilePath}");
                diagnostics.AppendLine($"  Expected crop: {cropWidth}x{cropHeight} @ {cropX},{cropY} -> {expectedCropPath}");
                diagnostics.AppendLine($"  Mean absolute RGB difference vs reference crop: {meanDiff:F4}");
            }

            diagnostics.AppendLine();
        }

        var resultPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        using var resultImage = Image.Load<Rgba32>(resultPath);
        var copiedResultPath = Path.Combine(outputDirectory, "stitched.png");
        File.Copy(resultPath, copiedResultPath, overwrite: true);
        diagnostics.AppendLine($"Final stitched image: {resultImage.Width}x{resultImage.Height} -> {copiedResultPath}");

        var diagnosticsPath = Path.Combine(outputDirectory, "diagnostics.txt");
        await File.WriteAllTextAsync(diagnosticsPath, diagnostics.ToString());

        TestContext.Progress.WriteLine($"Local cube_diorama tiled diagnostics written to: {diagnosticsPath}");
    }

    #endregion

    #region Helpers

    private async Task<string> RenderFrameDirectAsync(string blendFilePath, int frame, string outputBase, RenderOptionsData options)
    {
        var blenderExecutablePath = GetBlenderExecutablePath();
        var scriptPath = Path.Combine(Path.GetDirectoryName(outputBase)!, $"render_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllLinesAsync(scriptPath,
            [
                "import bpy",
                "scene = bpy.context.scene",
                "cycles = getattr(scene, 'cycles', None)",
                "scene.render.engine = 'CYCLES'",
                "scene.render.image_settings.file_format = 'PNG'",
                "scene.render.image_settings.color_mode = 'RGBA'",
                $"scene.render.resolution_x = {options.ResolutionX}",
                $"scene.render.resolution_y = {options.ResolutionY}",
                "scene.render.resolution_percentage = 100",
                $"scene.frame_set({frame})",
                "if cycles is not None:",
                $"    cycles.samples = {options.Samples}",
                $"    cycles.use_denoising = {(options.Denoise ? "True" : "False")}"
            ]);

            var args = $"-b \"{blendFilePath}\" -E CYCLES -o \"{outputBase}\" -F PNG --python-exit-code 1 --python \"{scriptPath}\" -f {frame}";

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = blenderExecutablePath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Direct Blender render failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");

            var renderedPath = $"{outputBase}{frame:0000}.png";
            if (!File.Exists(renderedPath))
                throw new InvalidOperationException($"Direct Blender render completed but output file was not found: {renderedPath}");

            return renderedPath;
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                try { File.Delete(scriptPath); }
                catch { }
            }
        }
    }

    #endregion
}
