using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Local diagnostic tests for the user-provided cube_diorama demo scene rendered directly by BlenderRunner.
/// These tests avoid cloud/bridge layers and persist artifacts for manual inspection.
/// </summary>
[TestFixture]
public sealed class BlenderRunnerCubeDioramaDiagnosticsTests
{
    #region Fields

    private BlenderRunner m_runner = null!;
    private string m_blenderExecutablePath = null!;
    private string m_outputDir = null!;
    private string m_scenePath = null!;
    private string m_solutionRoot = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                        ?? throw new DirectoryNotFoundException("Solution root not found.");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        m_scenePath = RenderTestAssetPaths.GetCubeDioramaScenePath(m_solutionRoot);
        if (!File.Exists(m_scenePath))
            Assert.Ignore($"Cube Diorama scene not found at {m_scenePath}");

        m_runner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!m_runner.IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir} — skip local diagnostics");

        m_blenderExecutablePath = typeof(BlenderRunner)
                                 .GetField("m_blenderPath", BindingFlags.Instance | BindingFlags.NonPublic)?
                                 .GetValue(m_runner) as string
                                 ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");
    }

    [SetUp]
    public void SetUp()
    {
        m_outputDir = Path.Combine(
            m_solutionRoot,
            "@Publish",
            "LiveTestOutputs",
            "LocalBlender",
            $"cube_diorama_{DateTime.UtcNow:yyyy-MM-dd-HH-mm-ss}");
        Directory.CreateDirectory(m_outputDir);
    }

    #endregion

    #region Tests

    [Test]
    [Explicit("Local diagnostic render for the user-provided cube_diorama scene using BlenderRunner only.")]
    public async Task RenderCubeDioramaStillAndAnalyzeAlphaTest()
    {
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 0,
            ResolutionX = 0,
            ResolutionY = 0
        };

        var outputBase = Path.Combine(m_outputDir, "cube_diorama_");
        var renderedPath = await m_runner.RenderFrameAsync(m_scenePath, 1, outputBase, options);

        Assert.That(File.Exists(renderedPath), Is.True, $"Rendered file not found: {renderedPath}");
        Assert.That(new FileInfo(renderedPath).Length, Is.GreaterThan(0));

        var stats = await AnalyzeRenderedOutputAsync(renderedPath, m_outputDir, "Cube Diorama scene-default render");

        Assert.That(stats.TotalPixels, Is.GreaterThan(0));
        Assert.That(stats.NonTransparentPixels, Is.GreaterThan(0), "Expected BlenderRunner to produce a visible non-transparent PNG for cube_diorama.");
        Assert.That(stats.MaxR, Is.GreaterThan((byte)32), "Expected BlenderRunner to recover a materially visible image for cube_diorama instead of a near-black output.");
    }

    [Test]
    [Explicit("Local diagnostic render for the user-provided cube_diorama scene using direct Blender CLI with explicit RGB and RGBA output modes.")]
    public async Task RenderCubeDioramaStillWithExplicitColorModesTest()
    {
        var rgbaDirectory = Path.Combine(m_outputDir, "rgba");
        Directory.CreateDirectory(rgbaDirectory);
        var rgbaRenderedPath = await RenderFrameDirectAsync(m_scenePath, 1, Path.Combine(rgbaDirectory, "cube_diorama_rgba_"), "RGBA");
        var rgbaStats = await AnalyzeRenderedOutputAsync(rgbaRenderedPath, rgbaDirectory, "Cube Diorama direct RGBA override");

        var rgbDirectory = Path.Combine(m_outputDir, "rgb");
        Directory.CreateDirectory(rgbDirectory);
        var rgbRenderedPath = await RenderFrameDirectAsync(m_scenePath, 1, Path.Combine(rgbDirectory, "cube_diorama_rgb_"), "RGB");
        var rgbStats = await AnalyzeRenderedOutputAsync(rgbRenderedPath, rgbDirectory, "Cube Diorama direct RGB override");

        TestContext.Progress.WriteLine($"Explicit RGBA NonTransparent={rgbaStats.NonTransparentPixels}; AverageA={rgbaStats.AverageA:F2}");
        TestContext.Progress.WriteLine($"Explicit RGB NonTransparent={rgbStats.NonTransparentPixels}; AverageA={rgbStats.AverageA:F2}");

        Assert.That(File.Exists(rgbaRenderedPath), Is.True);
        Assert.That(File.Exists(rgbRenderedPath), Is.True);
    }

    [Test]
    [Explicit("Local diagnostic render for the user-provided cube_diorama scene using direct Blender CLI with explicit color-management overrides.")]
    public async Task RenderCubeDioramaStillWithColorManagementOverridesTest()
    {
        var standardDirectory = Path.Combine(m_outputDir, "standard");
        Directory.CreateDirectory(standardDirectory);
        var standardRenderedPath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(standardDirectory, "cube_diorama_standard_"),
            "RGB",
            [
                "scene.display_settings.display_device = 'sRGB'",
                "scene.view_settings.view_transform = 'Standard'",
                "scene.view_settings.look = 'None'",
                "scene.view_settings.exposure = 0.0",
                "scene.view_settings.gamma = 1.0"
            ]);
        var standardStats = await AnalyzeRenderedOutputAsync(standardRenderedPath, standardDirectory, "Cube Diorama direct Standard/sRGB override");

        var exposedDirectory = Path.Combine(m_outputDir, "standard_exposed");
        Directory.CreateDirectory(exposedDirectory);
        var exposedRenderedPath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(exposedDirectory, "cube_diorama_standard_exposed_"),
            "RGB",
            [
                "scene.display_settings.display_device = 'sRGB'",
                "scene.view_settings.view_transform = 'Standard'",
                "scene.view_settings.look = 'None'",
                "scene.view_settings.exposure = 6.0",
                "scene.view_settings.gamma = 1.0"
            ]);
        var exposedStats = await AnalyzeRenderedOutputAsync(exposedRenderedPath, exposedDirectory, "Cube Diorama direct Standard/sRGB + exposure override");

        TestContext.Progress.WriteLine($"Standard override MaxRGB={standardStats.MaxR}/{standardStats.MaxG}/{standardStats.MaxB}; Average={standardStats.AverageR:F2}");
        TestContext.Progress.WriteLine($"Exposed override MaxRGB={exposedStats.MaxR}/{exposedStats.MaxG}/{exposedStats.MaxB}; Average={exposedStats.AverageR:F2}");

        Assert.That(File.Exists(standardRenderedPath), Is.True);
        Assert.That(File.Exists(exposedRenderedPath), Is.True);
    }

    [Test]
    [Explicit("Local scene introspection for the user-provided cube_diorama blend using Blender CLI only.")]
    public async Task InspectCubeDioramaSceneConfigurationTest()
    {
        var diagnosticsDirectory = Path.Combine(m_outputDir, "scene_inspection");
        Directory.CreateDirectory(diagnosticsDirectory);

        var diagnosticsOutput = await RunBlenderPythonAsync(
            m_scenePath,
            new List<string>
            {
                "import bpy, json",
                "scene = bpy.context.scene",
                "render = scene.render",
                "world = scene.world",
                "lights = []",
                "hidden_objects = []",
                "view_layers = []",
                "for obj in scene.objects:",
                "    if obj.type == 'LIGHT':",
                "        lights.append({'name': obj.name, 'light_type': getattr(obj.data, 'type', ''), 'energy': float(getattr(obj.data, 'energy', 0.0) or 0.0), 'hide_render': bool(obj.hide_render), 'location': [float(v) for v in obj.location]})",
                "    if bool(obj.hide_render):",
                "        hidden_objects.append({'name': obj.name, 'type': obj.type})",
                "for layer in scene.view_layers:",
                "    view_layers.append({'name': layer.name, 'use': bool(getattr(layer, 'use', True))})",
                "world_info = None",
                "if world is not None:",
                "    world_info = {'name': world.name, 'use_nodes': bool(getattr(world, 'use_nodes', False)), 'color': [float(v) for v in getattr(world, 'color', (0.0, 0.0, 0.0))], 'node_names': [node.name for node in getattr(getattr(world, 'node_tree', None), 'nodes', [])]}",
                "camera_info = None",
                "if scene.camera is not None:",
                "    camera_info = {'name': scene.camera.name, 'location': [float(v) for v in scene.camera.location], 'rotation_euler': [float(v) for v in scene.camera.rotation_euler], 'hide_render': bool(scene.camera.hide_render)}",
                "output = {'scene_name': scene.name, 'frame_current': int(scene.frame_current), 'frame_start': int(scene.frame_start), 'frame_end': int(scene.frame_end), 'camera': camera_info, 'lights': lights, 'hidden_objects': hidden_objects, 'hidden_object_count': len(hidden_objects), 'object_count': len(scene.objects), 'mesh_count': len([obj for obj in scene.objects if obj.type == 'MESH']), 'world': world_info, 'view_layers': view_layers, 'render': {'engine': render.engine, 'resolution_x': render.resolution_x, 'resolution_y': render.resolution_y, 'resolution_percentage': int(render.resolution_percentage), 'film_transparent': bool(getattr(render, 'film_transparent', False)), 'filepath': render.filepath, 'image_format': getattr(render.image_settings, 'file_format', ''), 'color_mode': getattr(render.image_settings, 'color_mode', ''), 'color_depth': getattr(render.image_settings, 'color_depth', ''), 'use_compositing': bool(getattr(render, 'use_compositing', False)), 'use_sequencer': bool(getattr(render, 'use_sequencer', False)), 'dither_intensity': float(getattr(render, 'dither_intensity', 0.0) or 0.0)}, 'display': {'display_device': getattr(scene.display_settings, 'display_device', ''), 'view_transform': getattr(scene.view_settings, 'view_transform', ''), 'look': getattr(scene.view_settings, 'look', ''), 'exposure': float(getattr(scene.view_settings, 'exposure', 0.0) or 0.0), 'gamma': float(getattr(scene.view_settings, 'gamma', 0.0) or 0.0)}}",
                "print('WIT_DIAGNOSTICS_START')",
                "print(json.dumps(output, indent=2))",
                "print('WIT_DIAGNOSTICS_END')"
            });

        var diagnosticsFilePath = Path.Combine(diagnosticsDirectory, "scene_inspection.json");
        await File.WriteAllTextAsync(diagnosticsFilePath, diagnosticsOutput);

        TestContext.Progress.WriteLine($"Cube Diorama scene inspection written to: {diagnosticsFilePath}");

        Assert.That(File.Exists(diagnosticsFilePath), Is.True);
    }

    [Test]
    [Explicit("Local diagnostic render for cube_diorama with sequencer disabled and view layers explicitly enabled.")]
    public async Task RenderCubeDioramaStillWithPipelineOverridesTest()
    {
        var sequencerOffDirectory = Path.Combine(m_outputDir, "sequencer_off");
        Directory.CreateDirectory(sequencerOffDirectory);
        var sequencerOffRenderedPath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(sequencerOffDirectory, "cube_diorama_sequencer_off_"),
            "RGB",
            [
                "scene.render.use_sequencer = False"
            ]);
        var sequencerOffStats = await AnalyzeRenderedOutputAsync(sequencerOffRenderedPath, sequencerOffDirectory, "Cube Diorama direct RGB + sequencer off");

        var fullOverrideDirectory = Path.Combine(m_outputDir, "sequencer_off_layers_on");
        Directory.CreateDirectory(fullOverrideDirectory);
        var fullOverrideRenderedPath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(fullOverrideDirectory, "cube_diorama_sequencer_off_layers_on_"),
            "RGB",
            [
                "scene.render.use_sequencer = False",
                "for layer in scene.view_layers:",
                "    layer.use = True"
            ]);
        var fullOverrideStats = await AnalyzeRenderedOutputAsync(fullOverrideRenderedPath, fullOverrideDirectory, "Cube Diorama direct RGB + sequencer off + layers on");

        TestContext.Progress.WriteLine($"Sequencer off MaxRGB={sequencerOffStats.MaxR}/{sequencerOffStats.MaxG}/{sequencerOffStats.MaxB}; Average={sequencerOffStats.AverageR:F2}");
        TestContext.Progress.WriteLine($"Sequencer off + layers on MaxRGB={fullOverrideStats.MaxR}/{fullOverrideStats.MaxG}/{fullOverrideStats.MaxB}; Average={fullOverrideStats.AverageR:F2}");

        Assert.That(File.Exists(sequencerOffRenderedPath), Is.True);
        Assert.That(File.Exists(fullOverrideRenderedPath), Is.True);
    }

    [Test]
    [Explicit("Local diagnostic tiled render for the full cube_diorama scene from @Data using BlenderRunner tile-task arguments.")]
    public async Task RenderCubeDioramaTileTaskReportsExpectedTileDimensionsTest()
    {
        var sceneResolution = await GetSceneResolutionAsync(m_scenePath);

        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 0,
            ResolutionX = sceneResolution.Width,
            ResolutionY = sceneResolution.Height
        };

        var tileTask = new RenderTaskData
        {
            Frame = 1,
            TileMinX = 0f,
            TileMaxX = 0.5f,
            TileMinY = 0f,
            TileMaxY = 0.5f,
            RenderMinX = 0f,
            RenderMaxX = 0.5f,
            RenderMinY = 0f,
            RenderMaxY = 0.5f,
            TaskIndex = 0,
            Options = options
        };

        var outputBase = Path.Combine(m_outputDir, "cube_diorama_tile_");
        var renderedPath = await m_runner.RenderFrameAsync(m_scenePath, 1, outputBase, options, default, tileTask);

        Assert.That(File.Exists(renderedPath), Is.True, $"Rendered tile file not found: {renderedPath}");
        Assert.That(new FileInfo(renderedPath).Length, Is.GreaterThan(0));

        var stats = await AnalyzeRenderedOutputAsync(renderedPath, m_outputDir, "Cube Diorama BlenderRunner tile render");
        var expectedWidth = sceneResolution.Width / 2;
        var expectedHeight = sceneResolution.Height / 2;

        TestContext.Progress.WriteLine($"Cube Diorama tile render expected dimensions: {expectedWidth}x{expectedHeight}");
        TestContext.Progress.WriteLine($"Cube Diorama tile render actual dimensions: {stats.Width}x{stats.Height}");

        Assert.That(stats.Width, Is.EqualTo(expectedWidth),
            $"Expected BlenderRunner tile render to produce a cropped tile-sized image for cube_diorama, but got width {stats.Width} instead of {expectedWidth}.");
        Assert.That(stats.Height, Is.EqualTo(expectedHeight),
            $"Expected BlenderRunner tile render to produce a cropped tile-sized image for cube_diorama, but got height {stats.Height} instead of {expectedHeight}.");
    }

    [Test]
    [Explicit("Local direct-Blender diagnostic comparison for one cube_diorama tile under default vs pipeline override settings.")]
    public async Task RenderCubeDioramaDirectTilePipelineVariantComparisonTest()
    {
        var sceneResolution = await GetSceneResolutionAsync(m_scenePath);
        var referenceDirectory = Path.Combine(m_outputDir, "reference");
        Directory.CreateDirectory(referenceDirectory);
        var referencePath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(referenceDirectory, "cube_diorama_reference_"),
            "RGB",
            [
                $"scene.render.resolution_x = {sceneResolution.Width}",
                $"scene.render.resolution_y = {sceneResolution.Height}",
                "scene.render.resolution_percentage = 100"
            ]);

        using var referenceImage = await Image.LoadAsync<Rgba32>(referencePath);
        var report = new StringBuilder();
        report.AppendLine($"Reference: {referencePath}");
        report.AppendLine($"Reference dimensions: {referenceImage.Width}x{referenceImage.Height}");
        report.AppendLine();

        var variants = new[]
        {
            new { Name = "default", Lines = new List<string>() },
            new { Name = "sequencer_off", Lines = new List<string> { "scene.render.use_sequencer = False" } },
            new { Name = "compositing_off", Lines = new List<string> { "scene.render.use_compositing = False" } },
            new { Name = "sequencer_and_compositing_off", Lines = new List<string> { "scene.render.use_sequencer = False", "scene.render.use_compositing = False" } }
        };

        foreach (var variant in variants)
        {
            var variantDirectory = Path.Combine(m_outputDir, variant.Name);
            Directory.CreateDirectory(variantDirectory);

            var tilePath = await RenderFrameDirectAsync(
                m_scenePath,
                1,
                Path.Combine(variantDirectory, "cube_diorama_tile_"),
                "RGB",
                BuildTilePythonLines(sceneResolution.Width, sceneResolution.Height, variant.Lines));

            using var tileImage = await Image.LoadAsync<Rgba32>(tilePath);
            var expectedCrop = referenceImage.Clone(me => me.Crop(new Rectangle(0, sceneResolution.Height / 2 - 8, sceneResolution.Width / 2 + 8, sceneResolution.Height / 2 + 8)));
            var expectedCropPath = Path.Combine(variantDirectory, "expected_crop.png");
            await expectedCrop.SaveAsPngAsync(expectedCropPath);

            var meanDiff = CalculateMeanAbsoluteRgbDifference(tileImage, expectedCrop);

            report.AppendLine($"Variant: {variant.Name}");
            report.AppendLine($"  Tile: {tilePath}");
            report.AppendLine($"  Tile dimensions: {tileImage.Width}x{tileImage.Height}");
            report.AppendLine($"  Expected crop: {expectedCropPath}");
            report.AppendLine($"  Mean absolute RGB difference: {meanDiff:F4}");
            report.AppendLine();
        }

        var reportPath = Path.Combine(m_outputDir, "tile-pipeline-variant-comparison.txt");
        await File.WriteAllTextAsync(reportPath, report.ToString());

        TestContext.Progress.WriteLine($"Cube Diorama tile pipeline variant report written to: {reportPath}");
        Assert.That(File.Exists(reportPath), Is.True);
    }

    [Test]
    [Explicit("Local diagnostic comparison of BlenderRunner tile render vs direct Blender border tile render for cube_diorama.")]
    public async Task RenderCubeDioramaBlenderRunnerTileMatchesDirectBorderTileTest()
    {
        var sceneResolution = await GetSceneResolutionAsync(m_scenePath);
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 0,
            ResolutionX = sceneResolution.Width,
            ResolutionY = sceneResolution.Height
        };

        var tileTask = new RenderTaskData
        {
            Frame = 1,
            TileMinX = 0f,
            TileMaxX = 0.5f,
            TileMinY = 0f,
            TileMaxY = 0.5f,
            RenderMinX = 0f,
            RenderMaxX = 0.50390625f,
            RenderMinY = 0f,
            RenderMaxY = 0.50390625f,
            TaskIndex = 0,
            Options = options
        };

        var directDirectory = Path.Combine(m_outputDir, "direct_tile");
        Directory.CreateDirectory(directDirectory);
        var referenceDirectory = Path.Combine(m_outputDir, "runner_vs_direct_reference");
        Directory.CreateDirectory(referenceDirectory);
        var referencePath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(referenceDirectory, "cube_diorama_reference_"),
            "RGB",
            [
                $"scene.render.resolution_x = {sceneResolution.Width}",
                $"scene.render.resolution_y = {sceneResolution.Height}",
                "scene.render.resolution_percentage = 100"
            ]);
        var directPath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(directDirectory, "cube_diorama_tile_"),
            "RGB",
            BuildTilePythonLines(sceneResolution.Width, sceneResolution.Height, []));

        var runnerDirectory = Path.Combine(m_outputDir, "runner_tile");
        Directory.CreateDirectory(runnerDirectory);
        var runnerPath = await m_runner.RenderFrameAsync(
            m_scenePath,
            1,
            Path.Combine(runnerDirectory, "cube_diorama_tile_"),
            options,
            default,
            tileTask);

        using var directImage = await Image.LoadAsync<Rgba32>(directPath);
        using var runnerImage = await Image.LoadAsync<Rgba32>(runnerPath);
        using var referenceImage = await Image.LoadAsync<Rgba32>(referencePath);
        var meanDiff = CalculateMeanAbsoluteRgbDifference(directImage, runnerImage);
        var bestMatch = FindBestMatchingCrop(referenceImage, runnerImage, step: 16, sampleStride: 16);
        var reportPath = Path.Combine(m_outputDir, "runner-vs-direct-tile.txt");
        await File.WriteAllTextAsync(
            reportPath,
            $"Reference full render: {referencePath}{Environment.NewLine}" +
            $"Direct tile: {directPath}{Environment.NewLine}" +
            $"Runner tile: {runnerPath}{Environment.NewLine}" +
            $"Direct dimensions: {directImage.Width}x{directImage.Height}{Environment.NewLine}" +
            $"Runner dimensions: {runnerImage.Width}x{runnerImage.Height}{Environment.NewLine}" +
            $"Mean absolute RGB difference: {meanDiff:F4}{Environment.NewLine}" +
            $"Best reference crop match for runner tile: x={bestMatch.OffsetX}, y={bestMatch.OffsetY}, mean diff={bestMatch.MeanAbsoluteRgbDifference:F4}{Environment.NewLine}");

        TestContext.Progress.WriteLine($"Cube Diorama runner vs direct tile report written to: {reportPath}");
        Assert.That(directImage.Width, Is.EqualTo(runnerImage.Width));
        Assert.That(directImage.Height, Is.EqualTo(runnerImage.Height));
        Assert.That(meanDiff, Is.EqualTo(0).Within(0.01d));
    }

    [Test]
    [Explicit("Local diagnostic comparison of BlenderRunner full-frame render vs direct Blender full-frame render for cube_diorama.")]
    public async Task RenderCubeDioramaBlenderRunnerFullFrameMatchesDirectFullFrameTest()
    {
        var sceneResolution = await GetSceneResolutionAsync(m_scenePath);
        var options = new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 0,
            ResolutionX = sceneResolution.Width,
            ResolutionY = sceneResolution.Height
        };

        var directDirectory = Path.Combine(m_outputDir, "direct_full");
        Directory.CreateDirectory(directDirectory);
        var directPath = await RenderFrameDirectAsync(
            m_scenePath,
            1,
            Path.Combine(directDirectory, "cube_diorama_full_"),
            "RGB",
            [
                $"scene.render.resolution_x = {sceneResolution.Width}",
                $"scene.render.resolution_y = {sceneResolution.Height}",
                "scene.render.resolution_percentage = 100"
            ]);

        var runnerDirectory = Path.Combine(m_outputDir, "runner_full");
        Directory.CreateDirectory(runnerDirectory);
        var runnerPath = await m_runner.RenderFrameAsync(
            m_scenePath,
            1,
            Path.Combine(runnerDirectory, "cube_diorama_full_"),
            options);

        using var directImage = await Image.LoadAsync<Rgba32>(directPath);
        using var runnerImage = await Image.LoadAsync<Rgba32>(runnerPath);
        var meanDiff = CalculateMeanAbsoluteRgbDifference(directImage, runnerImage);
        var reportPath = Path.Combine(m_outputDir, "runner-vs-direct-full.txt");
        await File.WriteAllTextAsync(
            reportPath,
            $"Direct full render: {directPath}{Environment.NewLine}" +
            $"Runner full render: {runnerPath}{Environment.NewLine}" +
            $"Direct dimensions: {directImage.Width}x{directImage.Height}{Environment.NewLine}" +
            $"Runner dimensions: {runnerImage.Width}x{runnerImage.Height}{Environment.NewLine}" +
            $"Mean absolute RGB difference: {meanDiff:F4}{Environment.NewLine}");

        TestContext.Progress.WriteLine($"Cube Diorama runner vs direct full report written to: {reportPath}");
        Assert.That(directImage.Width, Is.EqualTo(runnerImage.Width));
        Assert.That(directImage.Height, Is.EqualTo(runnerImage.Height));
        Assert.That(meanDiff, Is.EqualTo(0).Within(0.01d));
    }

    #endregion

    #region Tools

    private static RenderImageAnalysisStats AnalyzeImage(Image<Rgba32> image)
    {
        long nonBlackPixels = 0;
        long nonTransparentPixels = 0;
        long rgbVisiblePixels = 0;
        long fullyBlackOpaquePixels = 0;
        long totalR = 0;
        long totalG = 0;
        long totalB = 0;
        long totalA = 0;
        byte minR = byte.MaxValue;
        byte minG = byte.MaxValue;
        byte minB = byte.MaxValue;
        byte minA = byte.MaxValue;
        byte maxR = byte.MinValue;
        byte maxG = byte.MinValue;
        byte maxB = byte.MinValue;
        byte maxA = byte.MinValue;

        var totalPixels = (long)image.Width * image.Height;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                totalR += pixel.R;
                totalG += pixel.G;
                totalB += pixel.B;
                totalA += pixel.A;

                if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                    nonBlackPixels++;

                if (pixel.A != 0)
                    nonTransparentPixels++;

                if ((pixel.R != 0 || pixel.G != 0 || pixel.B != 0) && pixel.A == 0)
                    rgbVisiblePixels++;

                if (pixel.R == 0 && pixel.G == 0 && pixel.B == 0 && pixel.A == 255)
                    fullyBlackOpaquePixels++;

                minR = Math.Min(minR, pixel.R);
                minG = Math.Min(minG, pixel.G);
                minB = Math.Min(minB, pixel.B);
                minA = Math.Min(minA, pixel.A);
                maxR = Math.Max(maxR, pixel.R);
                maxG = Math.Max(maxG, pixel.G);
                maxB = Math.Max(maxB, pixel.B);
                maxA = Math.Max(maxA, pixel.A);
            }
        }

        return new RenderImageAnalysisStats
        {
            Width = image.Width,
            Height = image.Height,
            TotalPixels = totalPixels,
            NonBlackPixels = nonBlackPixels,
            NonTransparentPixels = nonTransparentPixels,
            NonBlackFullyTransparentPixels = rgbVisiblePixels,
            FullyBlackOpaquePixels = fullyBlackOpaquePixels,
            AverageR = totalR / (double)totalPixels,
            AverageG = totalG / (double)totalPixels,
            AverageB = totalB / (double)totalPixels,
            AverageA = totalA / (double)totalPixels,
            MinR = minR,
            MinG = minG,
            MinB = minB,
            MinA = minA,
            MaxR = maxR,
            MaxG = maxG,
            MaxB = maxB,
            MaxA = maxA
        };
    }

    private async Task<RenderImageAnalysisStats> AnalyzeRenderedOutputAsync(string renderedPath, string outputDirectory, string scenarioName)
    {
        using var image = await Image.LoadAsync<Rgba32>(renderedPath);
        var stats = AnalyzeImage(image);

        var rgbPreviewPath = Path.Combine(outputDirectory, "rgb-preview.png");
        var alphaPreviewPath = Path.Combine(outputDirectory, "alpha-preview.png");
        var lumaPreviewPath = Path.Combine(outputDirectory, "luma-preview.png");
        var analysisPath = Path.Combine(outputDirectory, "analysis.txt");

        await SaveRgbPreviewAsync(image, rgbPreviewPath);
        await SaveAlphaPreviewAsync(image, alphaPreviewPath);
        await SaveLumaPreviewAsync(image, lumaPreviewPath);
        await File.WriteAllTextAsync(
            analysisPath,
            BuildAnalysisText(m_scenePath, renderedPath, stats, rgbPreviewPath, alphaPreviewPath, lumaPreviewPath));

        TestContext.Progress.WriteLine($"{scenarioName} source: {m_scenePath}");
        TestContext.Progress.WriteLine($"{scenarioName} rendered output: {renderedPath}");
        TestContext.Progress.WriteLine($"{scenarioName} diagnostics written to: {outputDirectory}");
        TestContext.Progress.WriteLine($"{scenarioName} pixels: {stats.TotalPixels}; NonBlack={stats.NonBlackPixels}; NonTransparent={stats.NonTransparentPixels}");
        TestContext.Progress.WriteLine($"{scenarioName} average RGBA: {stats.AverageR:F2}, {stats.AverageG:F2}, {stats.AverageB:F2}, {stats.AverageA:F2}");

        Assert.That(File.Exists(analysisPath), Is.True);
        Assert.That(File.Exists(rgbPreviewPath), Is.True);
        Assert.That(File.Exists(alphaPreviewPath), Is.True);
        Assert.That(File.Exists(lumaPreviewPath), Is.True);

        return stats;
    }

    private async Task<string> RenderFrameDirectAsync(
        string blendFilePath,
        int frame,
        string outputBase,
        string colorMode,
        IReadOnlyList<string>? additionalPythonLines = null)
    {
        var pythonLines = new List<string>
        {
            "import bpy",
            "scene = bpy.context.scene",
            "scene.render.engine = 'CYCLES'",
            "scene.render.image_settings.file_format = 'PNG'",
            $"scene.render.image_settings.color_mode = '{colorMode}'",
            $"scene.frame_set({frame})"
        };

        if (additionalPythonLines != null)
            pythonLines.AddRange(additionalPythonLines);

        var scriptPath = Path.Combine(Path.GetDirectoryName(outputBase) ?? m_outputDir, $"render_{Guid.NewGuid():N}.py");
        await File.WriteAllLinesAsync(scriptPath, pythonLines);

        var args = $"-b \"{blendFilePath}\" -E CYCLES -o \"{outputBase}\" -F PNG --python-exit-code 1 --python \"{scriptPath}\" -f {frame}";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = m_blenderExecutablePath,
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

    private async Task<string> RunBlenderPythonAsync(string blendFilePath, IReadOnlyList<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_outputDir, $"scene_inspection_{Guid.NewGuid():N}.py");
        await File.WriteAllLinesAsync(scriptPath, pythonLines);

        var args = $"-b \"{blendFilePath}\" --python-exit-code 1 --python \"{scriptPath}\"";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = m_blenderExecutablePath,
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
            throw new InvalidOperationException($"Blender diagnostics failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");

        const string startMarker = "WIT_DIAGNOSTICS_START";
        const string endMarker = "WIT_DIAGNOSTICS_END";
        var startIndex = stdout.IndexOf(startMarker, StringComparison.Ordinal);
        var endIndex = stdout.IndexOf(endMarker, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
            throw new InvalidOperationException($"Blender diagnostics markers were not found. Stdout: {stdout}\nStderr: {stderr}");

        var json = stdout.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
        return json;
    }

    private async Task<(int Width, int Height)> GetSceneResolutionAsync(string blendFilePath)
    {
        var json = await RunBlenderPythonAsync(
            blendFilePath,
            [
                "import bpy, json",
                "scene = bpy.context.scene",
                "resolution_x = int(scene.render.resolution_x)",
                "resolution_y = int(scene.render.resolution_y)",
                "resolution_percentage = int(scene.render.resolution_percentage)",
                "payload = {'width': int(round(resolution_x * resolution_percentage / 100.0)), 'height': int(round(resolution_y * resolution_percentage / 100.0))}",
                "print('WIT_DIAGNOSTICS_START')",
                "print(json.dumps(payload))",
                "print('WIT_DIAGNOSTICS_END')"
            ]);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("width").GetInt32(),
            root.GetProperty("height").GetInt32());
    }

    private static IReadOnlyList<string> BuildTilePythonLines(int width, int height, IReadOnlyList<string> additionalLines)
    {
        var lines = new List<string>
        {
            $"scene.render.resolution_x = {width}",
            $"scene.render.resolution_y = {height}",
            "scene.render.resolution_percentage = 100",
            "scene.render.use_border = True",
            "scene.render.use_crop_to_border = True",
            "scene.render.border_min_x = 0.0",
            "scene.render.border_max_x = 0.50390625",
            "scene.render.border_min_y = 0.0",
            "scene.render.border_max_y = 0.50390625"
        };

        lines.AddRange(additionalLines);
        return lines;
    }

    private static double CalculateMeanAbsoluteRgbDifference(Image<Rgba32> left, Image<Rgba32> right)
    {
        if (left.Width != right.Width || left.Height != right.Height)
            throw new InvalidOperationException($"Image diff requires matching dimensions, got {left.Width}x{left.Height} and {right.Width}x{right.Height}.");

        long totalDifference = 0;
        long totalPixels = (long)left.Width * left.Height;

        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                var leftPixel = left[x, y];
                var rightPixel = right[x, y];
                totalDifference += Math.Abs(leftPixel.R - rightPixel.R)
                                   + Math.Abs(leftPixel.G - rightPixel.G)
                                   + Math.Abs(leftPixel.B - rightPixel.B);
            }
        }

        return totalPixels == 0 ? 0 : totalDifference / (double)totalPixels;
    }

    private static ReferenceCropMatch FindBestMatchingCrop(Image<Rgba32> reference, Image<Rgba32> tile, int step, int sampleStride)
    {
        if (tile.Width > reference.Width || tile.Height > reference.Height)
            throw new InvalidOperationException("Tile image cannot be larger than the reference image when searching for a matching crop.");

        var best = new ReferenceCropMatch
        {
            OffsetX = 0,
            OffsetY = 0,
            MeanAbsoluteRgbDifference = double.MaxValue
        };

        for (var offsetY = 0; offsetY <= reference.Height - tile.Height; offsetY += step)
        {
            for (var offsetX = 0; offsetX <= reference.Width - tile.Width; offsetX += step)
            {
                var diff = CalculateSampledMeanAbsoluteRgbDifference(reference, tile, offsetX, offsetY, sampleStride);
                if (diff < best.MeanAbsoluteRgbDifference)
                {
                    best = new ReferenceCropMatch
                    {
                        OffsetX = offsetX,
                        OffsetY = offsetY,
                        MeanAbsoluteRgbDifference = diff
                    };
                }
            }
        }

        return best;
    }

    private static double CalculateSampledMeanAbsoluteRgbDifference(Image<Rgba32> reference, Image<Rgba32> tile, int offsetX, int offsetY, int sampleStride)
    {
        long totalDifference = 0;
        long sampleCount = 0;

        for (var y = 0; y < tile.Height; y += sampleStride)
        {
            for (var x = 0; x < tile.Width; x += sampleStride)
            {
                var referencePixel = reference[x + offsetX, y + offsetY];
                var tilePixel = tile[x, y];
                totalDifference += Math.Abs(referencePixel.R - tilePixel.R)
                                   + Math.Abs(referencePixel.G - tilePixel.G)
                                   + Math.Abs(referencePixel.B - tilePixel.B);
                sampleCount++;
            }
        }

        return sampleCount == 0 ? 0 : totalDifference / (double)sampleCount;
    }

    private sealed class ReferenceCropMatch
    {
        public int OffsetX { get; init; }

        public int OffsetY { get; init; }

        public double MeanAbsoluteRgbDifference { get; init; }
    }

    private static async Task SaveRgbPreviewAsync(Image<Rgba32> source, string outputPath)
    {
        using var preview = source.Clone();
        for (var y = 0; y < preview.Height; y++)
        {
            for (var x = 0; x < preview.Width; x++)
            {
                var pixel = preview[x, y];
                preview[x, y] = new Rgba32(pixel.R, pixel.G, pixel.B, 255);
            }
        }

        await preview.SaveAsPngAsync(outputPath);
    }

    private static async Task SaveAlphaPreviewAsync(Image<Rgba32> source, string outputPath)
    {
        using var preview = new Image<Rgba32>(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var alpha = source[x, y].A;
                preview[x, y] = new Rgba32(alpha, alpha, alpha, 255);
            }
        }

        await preview.SaveAsPngAsync(outputPath);
    }

    private static async Task SaveLumaPreviewAsync(Image<Rgba32> source, string outputPath)
    {
        using var preview = new Image<Rgba32>(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source[x, y];
                var luma = (byte)Math.Clamp((int)Math.Round(0.2126 * pixel.R + 0.7152 * pixel.G + 0.0722 * pixel.B), 0, 255);
                preview[x, y] = new Rgba32(luma, luma, luma, 255);
            }
        }

        await preview.SaveAsPngAsync(outputPath);
    }

    private static string BuildAnalysisText(
        string scenePath,
        string renderedPath,
        RenderImageAnalysisStats stats,
        string rgbPreviewPath,
        string alphaPreviewPath,
        string lumaPreviewPath)
    {
        var text = new StringBuilder();
        text.AppendLine($"Scene: {scenePath}");
        text.AppendLine($"Rendered output: {renderedPath}");
        text.AppendLine($"Dimensions: {stats.Width}x{stats.Height}");
        text.AppendLine($"Total pixels: {stats.TotalPixels}");
        text.AppendLine($"Non-black pixels: {stats.NonBlackPixels}");
        text.AppendLine($"Non-transparent pixels: {stats.NonTransparentPixels}");
        text.AppendLine($"Non-black but fully transparent pixels: {stats.NonBlackFullyTransparentPixels}");
        text.AppendLine($"Fully black opaque pixels: {stats.FullyBlackOpaquePixels}");
        text.AppendLine($"Average RGBA: R={stats.AverageR:F4}; G={stats.AverageG:F4}; B={stats.AverageB:F4}; A={stats.AverageA:F4}");
        text.AppendLine($"Min RGBA: R={stats.MinR}; G={stats.MinG}; B={stats.MinB}; A={stats.MinA}");
        text.AppendLine($"Max RGBA: R={stats.MaxR}; G={stats.MaxG}; B={stats.MaxB}; A={stats.MaxA}");
        text.AppendLine($"RGB preview: {rgbPreviewPath}");
        text.AppendLine($"Alpha preview: {alphaPreviewPath}");
        text.AppendLine($"Luma preview: {lumaPreviewPath}");

        if (stats.NonBlackPixels == 0)
            text.AppendLine("Interpretation: RGB channels are completely black.");
        else if (stats.NonTransparentPixels == 0)
            text.AppendLine("Interpretation: RGB contains data, but the entire image is fully transparent.");
        else if (stats.NonBlackFullyTransparentPixels > 0)
            text.AppendLine("Interpretation: part of the RGB data exists only under zero alpha, so some viewers may appear to show an empty/black image.");
        else
            text.AppendLine("Interpretation: the image contains visible RGB content with non-zero alpha.");
 
        return text.ToString();
    }

    #endregion
}
