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
/// Explicit production-script tests that execute the bundled Render scripts end-to-end with real Blender.
/// </summary>
[TestFixture]
public sealed class RenderProductionScriptBlenderTests
{
    #region Constants

    private const string TEST_BLEND_SUBPATH = "@Prerequisites/test_scene.blend";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Fields

    private string m_blobStoragePath = null!;
    private string? m_blendPath;
    private RenderTestBlobService m_blobService = null!;
    private string? m_controllersPath;
    private string? m_cubeDioramaBlendPath;
    private string? m_scriptsPath;
    private string? m_solutionRoot;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        m_solutionRoot = RenderTestAssetPaths.FindSolutionRoot();
        if (m_solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot);
        if (blenderDir == null)
            Assert.Ignore("No supported Blender prerequisites for current OS/architecture");

        if (!new BlenderRunner(blenderDir, NullLogger.Instance).IsAvailable)
            Assert.Ignore($"Blender not found at {blenderDir}");

        m_controllersPath = RenderTestAssetPaths.FindControllersPath();
        if (m_controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_scriptsPath = RenderTestAssetPaths.FindBundledScriptsPath();
        if (m_scriptsPath == null)
            Assert.Ignore("@Scripts not found");

        m_blendPath = RenderTestAssetPaths.GetTestScenePath(m_solutionRoot);
        if (!File.Exists(m_blendPath))
            Assert.Ignore($"Test scene not found at {m_blendPath}");

        m_cubeDioramaBlendPath = RenderTestAssetPaths.GetCubeDioramaScenePath(m_solutionRoot);
        // cube_diorama existence is checked per-test (only ~3 tests need it) so the rest
        // of the fixture stays runnable when @Data/cube_diorama is absent locally.
    }

    private void RequireCubeDiorama()
    {
        if (m_cubeDioramaBlendPath == null || !File.Exists(m_cubeDioramaBlendPath))
            Assert.Ignore($"Cube Diorama scene not found at {m_cubeDioramaBlendPath}");
    }

    [SetUp]
    public void SetUp()
    {
        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witcloud_render_blobtest_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: m_controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: m_controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Production Script Tests

    [Test]
    public async Task BundledRenderStillScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStill.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(blobId!.Value)), Is.True);
        Assert.That(new FileInfo(m_blobService.GetStoredPath(blobId.Value)).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(
            m_blobService.GetStoredPath(blobId.Value),
            goldenPath,
            "RenderStill frame 1");
    }

    [Test]
    public async Task BundledRenderStillScriptRealRunProducesNonBlackImageTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStill.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);

        AssertImageIsNotSolidBlack(storedPath, "Bundled RenderStill frame 1");
    }

    [TestCase("RenderStillCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderStillEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderStillGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderStillScriptRealRunProducesNonBlackImageTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {benchmarkStillScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkStillScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions(engine));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        AssertImageIsNotSolidBlack(storedPath, $"{Path.GetFileNameWithoutExtension(scriptFileName)} frame 1");
    }

    [Test]
    public async Task BundledRenderFramesScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderFrames.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));

        foreach (var blobId in blobs)
        {
            Assert.That(blobId, Is.Not.Null);
            var storedPath = m_blobService.GetStoredPath(blobId!.Value);
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        }
    }

    [TestCase("RenderFramesCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderFramesEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderFramesGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderFramesScriptRealRunProducesExpectedFrameSetTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {benchmarkStillScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkStillScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(engine));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));

        var storedPaths = new List<string>(blobs.Count);
        foreach (var blobId in blobs)
        {
            Assert.That(blobId, Is.Not.Null);
            var storedPath = m_blobService.GetStoredPath(blobId!.Value);
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
            storedPaths.Add(storedPath);
        }

        AssertImageIsNotSolidBlack(storedPaths[0], $"{Path.GetFileNameWithoutExtension(scriptFileName)} frame 1");
    }

    [Test]
    public async Task BundledRenderStillTiledScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStillTiled.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(storedPath, goldenPath, "RenderStillTiled frame 1");
    }

    [Test]
    public async Task BundledRenderStillTiledOverlapScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderStillTiled.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(storedPath, goldenPath, "RenderStillTiled overlap frame 1");
    }

    [TestCase("RenderStillTiledCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderStillTiledEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderStillTiledGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderStillTiledScriptRealRunProducesNonBlackImageTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkStillScenePath = RenderTestAssetPaths.GetBenchmarkStillScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkStillScenePath))
            Assert.Ignore($"Benchmark still scene not found at {benchmarkStillScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkStillScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(engine),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        AssertImageIsNotSolidBlack(storedPath, $"{Path.GetFileNameWithoutExtension(scriptFileName)} frame 1");
    }

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
            "@Publish",
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

    private string GetBlenderExecutablePath()
    {
        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot!)
                         ?? throw new InvalidOperationException("No supported Blender prerequisites for current OS/architecture.");
        var blenderRunner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!blenderRunner.IsAvailable)
            throw new InvalidOperationException($"Blender not found at {blenderDir}");

        return typeof(BlenderRunner)
            .GetField("m_blenderPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .GetValue(blenderRunner) as string
            ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");
    }

    private async Task CreateBlendFileAsync(string blendPath, IEnumerable<string> pythonLines)
    {
        var scriptPath = Path.Combine(m_blobStoragePath, $"create_blend_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllLinesAsync(
                scriptPath,
                [
                    "import bpy",
                    "bpy.ops.wm.read_factory_settings(use_empty=True)",
                    .. pythonLines,
                    $"bpy.ops.wm.save_mainfile(filepath=r'{NormalizePythonPath(blendPath)}')"
                ]);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetBlenderExecutablePath(),
                    Arguments = $"-b --factory-startup --python-exit-code 1 --python \"{scriptPath}\"",
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
                throw new InvalidOperationException($"Blender scene creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
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

    private async Task CreateBlendFileWithCacheFileAsync(string blendPath, string cachePath, string cacheName)
    {
        await CreateBlendFileAsync(blendPath, BuildCacheFilePythonLines(cachePath, cacheName));
    }

    private async Task CreatePackedBlendCopyAsync(string sourceBlendPath, string packedBlendPath)
    {
        var scriptPath = Path.Combine(m_blobStoragePath, $"pack_blend_{Guid.NewGuid():N}.py");
        var lines = new[]
        {
            "import bpy",
            $"bpy.ops.wm.open_mainfile(filepath=r'{NormalizePythonPath(sourceBlendPath)}')",
            "bpy.ops.file.pack_all()",
            $"bpy.ops.wm.save_as_mainfile(filepath=r'{NormalizePythonPath(packedBlendPath)}', copy=True)"
        };

        try
        {
            await File.WriteAllLinesAsync(scriptPath, lines);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetBlenderExecutablePath(),
                    Arguments = $"-b --factory-startup --python-exit-code 1 --python \"{scriptPath}\"",
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
            {
                throw new InvalidOperationException(
                    $"Blender packed-copy creation failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");
            }
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

    private static IReadOnlyList<string> BuildCacheFilePythonLines(string cachePath, string cacheName)
    {
        return
        [
            $"cache_path = r'{NormalizePythonPath(cachePath)}'",
            $"cache_name = '{NormalizePythonPath(cacheName)}'",
            "cache_file = None",
            "cache_files = getattr(bpy.data, 'cache_files', None)",
            "load_fn = getattr(cache_files, 'load', None) if cache_files is not None else None",
            "if callable(load_fn):",
            "    try:",
            "        cache_file = load_fn(cache_path)",
            "    except Exception:",
            "        cache_file = None",
            "if cache_file is None:",
            "    new_fn = getattr(cache_files, 'new', None) if cache_files is not None else None",
            "    if callable(new_fn):",
            "        try:",
            "            cache_file = new_fn(cache_name)",
            "            cache_file.filepath = cache_path",
            "        except Exception:",
            "            cache_file = None",
            "if cache_file is None:",
            "    open_op = getattr(getattr(bpy.ops, 'cachefile', None), 'open', None)",
            "    if callable(open_op):",
            "        try:",
            "            open_op(filepath=cache_path)",
            "            cache_file = list(getattr(bpy.data, 'cache_files', []))[-1] if len(getattr(bpy.data, 'cache_files', [])) > 0 else None",
            "        except Exception:",
            "            cache_file = None",
            "if cache_file is None:",
            "    raise RuntimeError('CacheFile datablock creation is unavailable in this Blender runtime.')",
            "cache_file.use_fake_user = True"
        ];
    }

    private static string? FindTestFontPath()
    {
        var candidateDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            "/System/Library/Fonts",
            "/Library/Fonts"
        };

        foreach (var directory in candidateDirectories.Where(Directory.Exists))
        {
            try
            {
                var fontPath = Directory.EnumerateFiles(directory, "*.ttf", SearchOption.AllDirectories).FirstOrDefault()
                               ?? Directory.EnumerateFiles(directory, "*.otf", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(fontPath))
                    return fontPath;
            }
            catch
            {
                // Ignore inaccessible font folders and continue probing.
            }
        }

        return null;
    }

    private static string NormalizePythonPath(string path)
    {
        return path.Replace("\\", "/").Replace("'", "\\'");
    }

    private static void CreateTestWaveFile(string filePath)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const int sampleRate = 8000;
        const short samplesCount = 16;
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;
        var dataLength = samplesCount * blockAlign;

        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        for (var index = 0; index < samplesCount; index++)
            writer.Write((short)0);
    }

    private async Task CreateTestVideoFileAsync(string filePath)
    {
        var ffmpegPath = ResolveFfmpegExecutablePath();
        if (!File.Exists(ffmpegPath))
            Assert.Ignore($"ffmpeg not found at {ffmpegPath}");

        var framePath = Path.Combine(Path.GetDirectoryName(filePath)!, $"video_frame_{Guid.NewGuid():N}.png");

        try
        {
            using (var image = new Image<Rgba32>(2, 2))
                await image.SaveAsPngAsync(framePath);

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = $"-y -loop 1 -i \"{framePath}\" -t 1 -pix_fmt yuv420p \"{filePath}\"",
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
            if (process.ExitCode != 0 || !File.Exists(filePath))
                Assert.Ignore($"ffmpeg could not create a test video file. Stdout: {stdout} Stderr: {stderr}");
        }
        finally
        {
            if (File.Exists(framePath))
            {
                try { File.Delete(framePath); }
                catch { }
            }
        }
    }

    private static string ResolveFfmpegExecutablePath()
    {
        var resolverType = typeof(BlenderRunner).Assembly.GetType("OutWit.Controller.Render.Utils.RenderBinaryResolver")
                           ?? throw new InvalidOperationException("Failed to resolve RenderBinaryResolver type.");
        var resolveRootMethod = resolverType.GetMethod("ResolveFfmpegRoot", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to resolve ResolveFfmpegRoot method.");
        var resolvePathMethod = resolverType.GetMethod("ResolveFfmpegPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                                ?? throw new InvalidOperationException("Failed to resolve ResolveFfmpegPath method.");

        var ffmpegRoot = resolveRootMethod.Invoke(null, [typeof(BlenderRunner).Assembly.Location]) as string
                         ?? throw new InvalidOperationException("Failed to resolve ffmpeg root.");

        return resolvePathMethod.Invoke(null, [ffmpegRoot]) as string
               ?? throw new InvalidOperationException("Failed to resolve ffmpeg executable path.");
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

    [Test]
    public async Task BundledRenderVideoScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderVideo.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(videoBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(storedPath, Does.EndWith(".mp4"));
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderVideoPath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(
            storedPath,
            goldenPath,
            "RenderVideo frames 1-3");
    }

    [TestCase("RenderVideoCycles.wit", RenderEngine.Cycles)]
    [TestCase("RenderVideoEevee.wit", RenderEngine.Eevee)]
    [TestCase("RenderVideoGreasePencil.wit", RenderEngine.GreasePencil)]
    public async Task BundledEngineSpecificRenderVideoScriptRealRunProducesVideoBlobTest(string scriptFileName, RenderEngine engine)
    {
        var benchmarkVideoScenePath = RenderTestAssetPaths.GetBenchmarkVideoScenePath(m_solutionRoot!);
        if (!File.Exists(benchmarkVideoScenePath))
            Assert.Ignore($"Benchmark video scene not found at {benchmarkVideoScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, scriptFileName));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(benchmarkVideoScenePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(engine),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(videoBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(storedPath, Does.EndWith(".mp4"));
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task BundledRenderSceneFramesScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneFrames.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task BundledRenderSceneFramesLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneFramesLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobs = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(blobs, Is.Not.Null);
        Assert.That(blobs!, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task BundledRenderSceneStillScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStill.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(blobId!.Value)), Is.True);
    }

    [Test]
    public async Task BundledRenderSceneStillLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);
        Assert.That(File.Exists(m_blobService.GetStoredPath(blobId!.Value)), Is.True);
    }

    [Test]
    public async Task BundledRenderSceneStillTiledScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiled.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(storedPath, goldenPath, "RenderSceneStillTiled frame 1");
    }

    [Test]
    public async Task BundledRenderSceneStillTiledOverlapScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiled.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(storedPath, goldenPath, "RenderSceneStillTiled overlap frame 1");
    }

    [Test]
    public async Task BundledRenderSceneStillTiledLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiledLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(storedPath, goldenPath, "RenderSceneStillTiledLarge frame 1");
    }

    [Test]
    public async Task BundledRenderSceneStillTiledLargeOverlapScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneStillTiledLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var blobId = (Guid?)job.Variables["result"].Value;
        Assert.That(blobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(blobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));

        var goldenPath = RenderTestAssetPaths.GetGoldenRenderStillFramePath(m_solutionRoot!);
        RenderGoldenFileAssert.AssertMatchesOrUpdate(storedPath, goldenPath, "RenderSceneStillTiledLarge overlap frame 1");
    }

    [Test]
    public async Task BundledRenderSceneVideoScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneVideo.wit"));
        var job = m_engine.Compile(script);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            await CreateInlineSceneAsync(m_blendPath!),
            1,
            3,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);
    }

    [Test]
    public async Task BundledRenderSceneVideoLargeScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderSceneVideoLarge.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId),
            1,
            3,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var videoBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(videoBlobId, Is.Not.Null);
    }

    [Test]
    public async Task RenderBlenderVersionRealRunTest()
    {
        var script = """
                     Job:BlenderVersionDiag()
                     {
                         String:version = Render.BlenderVersion();
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var version = job.Variables["version"].Value as string;
        Assert.That(version, Is.Not.Null.And.Not.Empty);
        Assert.That(version!.StartsWith("Blender", StringComparison.OrdinalIgnoreCase), Is.True);
    }

    [Test]
    public async Task BundledRenderBlenderVersionScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderBlenderVersion.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var version = job.Variables["result"].Value as string;
        Assert.That(version, Is.Not.Null.And.Not.Empty);
        Assert.That(version!.StartsWith("Blender", StringComparison.OrdinalIgnoreCase), Is.True);
    }

    [Test]
    public async Task RenderRuntimeDiagnosticsRealRunTest()
    {
        var script = """
                     Job:RuntimeDiag()
                     {
                         RenderRuntimeDiagnostics:info = Render.RuntimeDiagnostics();
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderRuntimeDiagnosticsData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.RuntimeTarget, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostics.BlenderAvailable, Is.True);
        Assert.That(diagnostics.BlenderVersion, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostics.FfmpegAvailable, Is.True);
        Assert.That(diagnostics.FfprobeAvailable, Is.True);
        Assert.That(diagnostics.SupportsCenterPriorityCrop, Is.True);
        Assert.That(diagnostics.SupportsAlphaBlend, Is.True);
    }

    [Test]
    public async Task BundledRenderRuntimeDiagnosticsScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderRuntimeDiagnostics.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderRuntimeDiagnosticsData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.RuntimeTarget, Is.Not.Null.And.Not.Empty);
        Assert.That(diagnostics.BlenderAvailable, Is.True);
        Assert.That(diagnostics.FfmpegAvailable, Is.True);
        Assert.That(diagnostics.FfprobeAvailable, Is.True);
    }

    [Test]
    public async Task RenderPreflightStillTiledRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderPreflightStillTiled:info = Render.PreflightStillTiled(tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.RequestedBlendMode, Is.EqualTo(TileBlendMode.AlphaBlend));
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightVideoRealRunTest()
    {
        var script = """
                     Job:PreflightVideoDiag(RenderOptions:options, VideoOptions:video)
                     {
                         RenderPreflightVideo:info = Render.PreflightVideo(options, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:frame, Int:startFrame, Int:endFrame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions, VideoOptions:video)
                     {
                         RenderPreflight:info = Render.Preflight(frame, startFrame, endFrame, tilesX, tilesY, options, tileOptions, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            1,
            3,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.RuntimeDiagnostics, Is.Not.Null);
        Assert.That(diagnostics.Still, Is.Not.Null);
        Assert.That(diagnostics.Frames, Is.Not.Null);
        Assert.That(diagnostics.StillTiled, Is.Not.Null);
        Assert.That(diagnostics.Video, Is.Not.Null);
        Assert.That(diagnostics.CanRenderAll, Is.True);
        Assert.That(diagnostics.Still!.CanRender, Is.True);
        Assert.That(diagnostics.Frames!.CanRender, Is.True);
        Assert.That(diagnostics.StillTiled!.CanRender, Is.True);
        Assert.That(diagnostics.Video!.CanRender, Is.True);
    }

    [Test]
    public async Task RenderPreflightReportsInvalidRequestsRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:frame, Int:startFrame, Int:endFrame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions, VideoOptions:video)
                     {
                         RenderPreflight:info = Render.Preflight(frame, startFrame, endFrame, tilesX, tilesY, options, tileOptions, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            10,
            5,
            2,
            2,
            new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = 64,
                ResolutionY = 64
            },
            CreateTileOptions(32, TileBlendMode.AlphaBlend),
            new VideoOptionsData
            {
                FrameRate = 0,
                ConstantRateFactor = 60
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRenderAll, Is.False);
        Assert.That(diagnostics.Frames!.CanRender, Is.False);
        Assert.That(diagnostics.Frames.Issues.Any(me => me.Contains("endFrame", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Still!.CanRender, Is.False);
        Assert.That(diagnostics.Still.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.StillTiled!.CanRender, Is.False);
        Assert.That(diagnostics.StillTiled.Issues.Any(me => me.Contains("core tile size", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Video!.CanRender, Is.False);
        Assert.That(diagnostics.Video.Issues.Any(me => me.Contains("FrameRate", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflight.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            1,
            3,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRenderAll, Is.True);
        Assert.That(diagnostics.Still, Is.Not.Null);
        Assert.That(diagnostics.Frames, Is.Not.Null);
        Assert.That(diagnostics.StillTiled, Is.Not.Null);
        Assert.That(diagnostics.Video, Is.Not.Null);
    }

    [Test]
    public async Task BundledRenderPreflightFramesScriptReportsInvalidRangeRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightFrames.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            10,
            5,
            new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = -64,
                ResolutionY = -64
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("endFrame", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ResolutionX", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightStillScriptReportsInvalidOptionsRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStill.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = -64,
                ResolutionY = -64
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ResolutionX", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightStillScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStill.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task BundledRenderPreflightVideoScriptReportsInvalidOptionsRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightVideo.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Engine = RenderEngine.Cycles,
                Samples = 4,
                ResolutionX = 64,
                ResolutionY = 64
            },
            new VideoOptionsData
            {
                FrameRate = 0,
                ConstantRateFactor = 60
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("FrameRate", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ConstantRateFactor", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("PNG and JPEG", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task RenderPreflightFramesRealRunTest()
    {
        var script = """
                     Job:PreflightFramesDiag(Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderPreflightFrames:info = Render.PreflightFrames(startFrame, endFrame, options);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightFramesReportsInvalidRangeRealRunTest()
    {
        var script = """
                     Job:PreflightFramesDiag(Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         RenderPreflightFrames:info = Render.PreflightFrames(startFrame, endFrame, options);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            10,
            5,
            new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = -1,
                ResolutionX = -64,
                ResolutionY = -64
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("endFrame", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ResolutionX", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("Samples", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightFramesScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightFrames.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            1,
            3,
            CreateOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightFramesData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightVideoReportsInvalidOptionsRealRunTest()
    {
        var script = """
                     Job:PreflightVideoDiag(RenderOptions:options, VideoOptions:video)
                     {
                         RenderPreflightVideo:info = Render.PreflightVideo(options, video);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderOptionsData
            {
                Format = RenderFormat.EXR,
                Engine = RenderEngine.Cycles,
                Samples = 4,
                ResolutionX = 64,
                ResolutionY = 64
            },
            new VideoOptionsData
            {
                FrameRate = 0,
                ConstantRateFactor = 60
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("FrameRate", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("ConstantRateFactor", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("PNG and JPEG", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightVideoScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightVideo.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateOptions(),
            CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightVideoData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task RenderPreflightStillTiledReportsInvalidOverlapRealRunTest()
    {
        var script = """
                     Job:PreflightDiag(Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         RenderPreflightStillTiled:info = Render.PreflightStillTiled(tilesX, tilesY, options, tileOptions);
                     }
                     """;

        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(32, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["info"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("core tile size", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderPreflightStillTiledScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStillTiled.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(4, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.True);
        Assert.That(diagnostics.RequestedBlendMode, Is.EqualTo(TileBlendMode.AlphaBlend));
        Assert.That(diagnostics.Issues, Is.Empty);
    }

    [Test]
    public async Task BundledRenderPreflightStillTiledScriptReportsInvalidOverlapRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderPreflightStillTiled.wit"));
        var job = m_engine.Compile(script);
        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            2,
            2,
            CreateOptions(),
            CreateTileOptions(32, TileBlendMode.AlphaBlend));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var diagnostics = job.Variables["result"].Value as RenderPreflightStillTiledData;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.That(diagnostics!.CanRender, Is.False);
        Assert.That(diagnostics.Issues.Any(me => me.Contains("core tile size", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task RenderValidateBlendRealRunTest()
    {
        var script = """
                     Job:ValidateBlendDiag(Blob:scene)
                     {
                         String:result = Render.ValidateBlend(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);
        var status = await m_engine.ScheduleAndWaitAsync(job, sceneBlobId);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings, Is.Empty);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptAcceptsPackedImageBlendCopyRealRunTest()
    {
        var imagePath = Path.Combine(m_blobStoragePath, "external_texture.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imagePath);

        var sourceBlendPath = Path.Combine(m_blobStoragePath, "scene_with_external_image.blend");
        await CreateBlendFileAsync(
            sourceBlendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(imagePath)}')",
                "image.use_fake_user = True"
            ]);

        var packedBlendPath = Path.Combine(m_blobStoragePath, "scene_with_packed_external_image.blend");
        await CreatePackedBlendCopyAsync(sourceBlendPath, packedBlendPath);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(packedBlendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("image asset", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsCacheFileAttachmentBlobRealRunTest()
    {
        var cachePath = Path.Combine(m_blobStoragePath, "external_cache.abc");
        await File.WriteAllTextAsync(cachePath, "outwit-test");

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_cache_file.blend");
        try
        {
            await CreateBlendFileWithCacheFileAsync(blendPath, cachePath, "ExternalCache");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("CacheFile datablock creation is unavailable", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("The current Blender runtime does not expose a stable cache_file creation path for bundled script tests.");
            return;
        }

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(cachePath);
        File.Delete(cachePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "CacheFile",
                        BlobId = attachmentBlobId,
                        OriginalPath = cachePath,
                        RelativePath = "deps/cache-files/external_cache.abc",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("external cache file", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsFontAttachmentBlobRealRunTest()
    {
        var sourceFontPath = FindTestFontPath();
        if (sourceFontPath == null)
            Assert.Ignore("No test font was found on the current machine.");

        var externalFontPath = Path.Combine(m_blobStoragePath, Path.GetFileName(sourceFontPath));
        File.Copy(sourceFontPath, externalFontPath, overwrite: true);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_font.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"font = bpy.data.fonts.load(r'{NormalizePythonPath(externalFontPath)}')",
                "font.use_fake_user = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(externalFontPath);
        File.Delete(externalFontPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Font",
                        BlobId = attachmentBlobId,
                        OriginalPath = externalFontPath,
                        RelativePath = "deps/fonts/attached-font.ttf",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("external font", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsImageSequenceAttachmentBlobsRealRunTest()
    {
        var externalSequenceDirectory = Path.Combine(m_blobStoragePath, "external-image-sequence");
        Directory.CreateDirectory(externalSequenceDirectory);
        var externalFramePaths = new List<string>();
        for (var index = 1; index <= 2; index++)
        {
            var framePath = Path.Combine(externalSequenceDirectory, $"plate_{index:0000}.png");
            using var image = new Image<Rgba32>(1, 1);
            await image.SaveAsPngAsync(framePath);
            externalFramePaths.Add(framePath);
        }

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_image_sequence.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"image = bpy.data.images.load(r'{NormalizePythonPath(externalFramePaths[0])}')",
                "image.source = 'SEQUENCE'",
                "image.use_fake_user = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachments = externalFramePaths
            .Select(me => new RenderSceneAttachmentRefData
            {
                Kind = "ImageSequenceFrame",
                BlobId = m_blobService.UploadFileAsync(me).GetAwaiter().GetResult(),
                OriginalPath = me,
                RelativePath = $"deps/image-sequences/Plate/{Path.GetFileName(me)}",
                PackagingStrategy = "SceneAttachmentBlob"
            })
            .ToArray();
        Directory.Delete(externalSequenceDirectory, recursive: true);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(sceneBlobId, attachments));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("image sequence", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsLinkedLibraryAttachmentBlobRealRunTest()
    {
        var libraryBlendPath = Path.Combine(m_blobStoragePath, "library.blend");
        await CreateBlendFileAsync(
            libraryBlendPath,
            [
                "mesh = bpy.data.meshes.new('LibraryMesh')",
                "obj = bpy.data.objects.new('LibraryCube', mesh)",
                "bpy.context.scene.collection.objects.link(obj)"
            ]);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_library.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"library_path = r'{NormalizePythonPath(libraryBlendPath)}'",
                "with bpy.data.libraries.load(library_path, link=True) as (data_from, data_to):",
                "    data_to.objects = ['LibraryCube']",
                "for obj in data_to.objects:",
                "    if obj is not None:",
                "        bpy.context.scene.collection.objects.link(obj)"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(libraryBlendPath);
        File.Delete(libraryBlendPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "LinkedLibrary",
                        BlobId = attachmentBlobId,
                        OriginalPath = libraryBlendPath,
                        RelativePath = "deps/linked-libraries/library.blend",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsVolumeAttachmentBlobRealRunTest()
    {
        var externalVolumePath = Path.Combine(m_blobStoragePath, "external_volume.vdb");
        await File.WriteAllTextAsync(externalVolumePath, "outwit-test");

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_external_volume.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "volume = bpy.data.volumes.new('ExternalVolume')",
                $"volume.filepath = r'{NormalizePythonPath(externalVolumePath)}'",
                "volume.use_fake_user = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var attachmentBlobId = await m_blobService.UploadFileAsync(externalVolumePath);
        File.Delete(externalVolumePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Volume",
                        BlobId = attachmentBlobId,
                        OriginalPath = externalVolumePath,
                        RelativePath = "deps/volumes/external_volume.vdb",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("external volume", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRemapsTransferredMediaAttachmentsRealRunTest()
    {
        var movieClipDirectory = Path.Combine(m_blobStoragePath, "movie-clip-sequence");
        Directory.CreateDirectory(movieClipDirectory);
        var movieClipPath = Path.Combine(movieClipDirectory, "clip_0001.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(movieClipPath);

        var imageStripDirectory = Path.Combine(m_blobStoragePath, "vse-image-strip");
        Directory.CreateDirectory(imageStripDirectory);
        var imageStripPath = Path.Combine(imageStripDirectory, "frame_0001.png");
        using (var image = new Image<Rgba32>(1, 1))
            await image.SaveAsPngAsync(imageStripPath);

        var soundPath = Path.Combine(m_blobStoragePath, "media.wav");
        CreateTestWaveFile(soundPath);
        var movieStripPath = Path.Combine(m_blobStoragePath, "media.mp4");
        await CreateTestVideoFileAsync(movieStripPath);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_transferred_media.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                $"clip = bpy.data.movieclips.load(r'{NormalizePythonPath(movieClipPath)}')",
                "clip.use_fake_user = True",
                "scene = bpy.context.scene",
                "editor = scene.sequence_editor_create()",
                $"editor.strips.new_image('Image Strip', r'{NormalizePythonPath(imageStripPath)}', 1, 1)",
                $"editor.strips.new_sound('Sound Strip', r'{NormalizePythonPath(soundPath)}', 2, 1)",
                $"editor.strips.new_movie('Movie Strip', r'{NormalizePythonPath(movieStripPath)}', 3, 1)"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var movieClipBlobId = await m_blobService.UploadFileAsync(movieClipPath);
        var imageStripBlobId = await m_blobService.UploadFileAsync(imageStripPath);
        var soundBlobId = await m_blobService.UploadFileAsync(soundPath);
        var movieStripBlobId = await m_blobService.UploadFileAsync(movieStripPath);

        File.Delete(movieClipPath);
        File.Delete(imageStripPath);
        File.Delete(soundPath);
        File.Delete(movieStripPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            CreateSceneRef(
                sceneBlobId,
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "MovieClip",
                        BlobId = movieClipBlobId,
                        OriginalPath = movieClipPath,
                        RelativePath = "deps/movie-clips/clip_0001.png",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "VseImageStripFrame",
                        BlobId = imageStripBlobId,
                        OriginalPath = imageStripPath,
                        RelativePath = "deps/vse/image-strips/Image_Strip/frame_0001.png",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Sound",
                        BlobId = soundBlobId,
                        OriginalPath = soundPath,
                        RelativePath = "deps/sounds/media.wav",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "VseSoundStrip",
                        BlobId = soundBlobId,
                        OriginalPath = soundPath,
                        RelativePath = "deps/vse/sound-strips/Sound_Strip/media.wav",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "VseMovieStrip",
                        BlobId = movieStripBlobId,
                        OriginalPath = movieStripPath,
                        RelativePath = "deps/vse/movie-strips/Movie_Strip/media.mp4",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("movie clip", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("external sound", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE image strip", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE sound strip", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE movie strip", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptRealRunTest()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(m_blendPath!);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings, Is.Empty);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsFluidCacheIssuesForLavaSceneRealRunTest()
    {
        var lavaScenePath = Path.Combine(m_solutionRoot!, "@Data", "lava_fluid-viscosity-demo.blend");
        if (!File.Exists(lavaScenePath))
            Assert.Ignore($"Lava scene not found at {lavaScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(lavaScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("Fluid domain", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("cache", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsIssuesForFlipVsApicSimulationFixtureRealRunTest()
    {
        var scenePath = Path.Combine(m_solutionRoot!, "@Data", "fluid-simulation_flip_vs_apic_solver.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        AssertSimulationFixtureBlocked(validation!);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsIssuesForClothInternalAirPressureFixtureRealRunTest()
    {
        var scenePath = Path.Combine(m_solutionRoot!, "@Data", "cloth_internal_air_pressure.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        AssertSimulationFixtureBlocked(validation!);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsIssuesForClothInnerSpringsFixtureRealRunTest()
    {
        var scenePath = Path.Combine(m_solutionRoot!, "@Data", "cloth_inner_springs.blend");
        if (!File.Exists(scenePath))
            Assert.Ignore($"Simulation fixture not found at {scenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(scenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        AssertSimulationFixtureBlocked(validation!);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsFluidCacheDirectoryAndMissingBakedSimulationDataRealRunTest()
    {
        var cacheDirectory = Path.Combine(m_blobStoragePath, "fluid-cache");
        Directory.CreateDirectory(cacheDirectory);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_fluid_cache_directory.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "modifier = obj.modifiers.new(name='Fluid', type='FLUID')",
                "modifier.fluid_type = 'DOMAIN'",
                "domain = modifier.domain_settings",
                $"domain.cache_directory = r'{NormalizePythonPath(cacheDirectory)}'"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("external cache directory", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("requires baked simulation data", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsFluidMeshCacheRequirementRealRunTest()
    {
        var cacheDirectory = Path.Combine(m_blobStoragePath, "fluid-mesh-cache");
        Directory.CreateDirectory(cacheDirectory);

        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_fluid_mesh_cache_requirement.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "modifier = obj.modifiers.new(name='Fluid', type='FLUID')",
                "modifier.fluid_type = 'DOMAIN'",
                "domain = modifier.domain_settings",
                $"domain.cache_directory = r'{NormalizePythonPath(cacheDirectory)}'",
                "domain.use_mesh = True"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("requires baked mesh cache", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsParticleSimulationIssueRealRunTest()
    {
        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_particle_simulation.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "obj.modifiers.new(name='Particles', type='PARTICLE_SYSTEM')"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("particle simulation", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsGeometryCacheIssueRealRunTest()
    {
        var blendPath = Path.Combine(m_blobStoragePath, "scene_with_geometry_cache.blend");
        await CreateBlendFileAsync(
            blendPath,
            [
                "bpy.ops.mesh.primitive_cube_add()",
                "obj = bpy.context.active_object",
                "obj.modifiers.new(name='GeoCache', type='MESH_SEQUENCE_CACHE')"
            ]);

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("geometry cache", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsWarningsForUdimMonsterSceneRealRunTest()
    {
        var udimScenePath = Path.Combine(m_solutionRoot!, "@Data", "UDIM_monster", "udim-monster.blend");
        if (!File.Exists(udimScenePath))
            Assert.Ignore($"UDIM monster scene not found at {udimScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(udimScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("UDIM", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("UDIM image set", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptReportsWarningsForVseMediaSceneRealRunTest()
    {
        var vseScenePath = Path.Combine(m_solutionRoot!, "@Data", "vse_media-transform", "vse_media-transform.blend");
        if (!File.Exists(vseScenePath))
            Assert.Ignore($"VSE media scene not found at {vseScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(vseScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.True);
        Assert.That(validation.Issues, Is.Empty);
        Assert.That(validation.Warnings.Any(me => me.Contains("VSE image strip", StringComparison.OrdinalIgnoreCase)), Is.True);
    }

    [Test]
    public async Task BundledRenderValidateBlendScriptDoesNotReportFalseLinkedLibraryFindingsForCowboiStorytoolsRealRunTest()
    {
        var cowboiScenePath = Path.Combine(m_solutionRoot!, "@Data", "cowboi_storytools.blend");
        if (!File.Exists(cowboiScenePath))
            Assert.Ignore($"cowboi_storytools scene not found at {cowboiScenePath}");

        var script = await File.ReadAllTextAsync(Path.Combine(m_scriptsPath!, "RenderValidateBlend.wit"));
        var job = m_engine.Compile(script);
        var sceneBlobId = m_blobService.RegisterExistingFile(cowboiScenePath);
        var status = await m_engine.ScheduleAndWaitAsync(job, CreateSceneRef(sceneBlobId));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["result"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation!.IsValid, Is.False);
        Assert.That(validation.Issues.Any(me => me.Contains("Image sequence dependency", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(validation.Issues.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
        Assert.That(validation.Warnings.Any(me => me.Contains("linked library", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    #endregion

    #region Tools

    private static RenderSceneRefData CreateSceneRef(Guid sceneBlobId, IReadOnlyList<RenderSceneAttachmentRefData>? attachedFiles = null)
    {
        return new RenderSceneRefData
        {
            BlendBlobId = sceneBlobId,
            AttachedFiles = attachedFiles?.Select(me => (RenderSceneAttachmentRefData)me.Clone()).ToList() ?? []
        };
    }

    private static async Task<RenderSceneData> CreateInlineSceneAsync(string blendFilePath)
    {
        return new RenderSceneData
        {
            FileName = Path.GetFileName(blendFilePath),
            BlendFileBytes = await File.ReadAllBytesAsync(blendFilePath)
        };
    }

    #endregion

    private static RenderOptionsData CreateOptions(RenderEngine engine = RenderEngine.Cycles)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = engine,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64
        };
    }

    private static VideoOptionsData CreateVideoOptions()
    {
        return new VideoOptionsData
        {
            FrameRate = 24,
            ConstantRateFactor = 23
        };
    }

    private static TileOptionsData CreateTileOptions(int overlapPx = 0, TileBlendMode blendMode = TileBlendMode.CenterPriorityCrop)
    {
        return new TileOptionsData
        {
            OverlapPx = overlapPx,
            BlendMode = blendMode
        };
    }

    private static void AssertImageIsNotSolidBlack(string imagePath, string context)
    {
        using var image = Image.Load<Rgba32>(imagePath);

        long nonBlackPixels = 0;
        long totalPixels = (long)image.Width * image.Height;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                    nonBlackPixels++;
            }
        }

        Assert.That(totalPixels, Is.GreaterThan(0), $"{context}: image contains no pixels.");
        Assert.That(nonBlackPixels, Is.GreaterThan(0), $"{context}: rendered image is completely black.");
    }

    private static void AssertSimulationFixtureBlocked(RenderValidateBlendData validation)
    {
        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Issues, Is.Not.Empty);
            Assert.That(
                validation.Issues.Any(me => me.Contains("baked simulation data", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("baked mesh cache", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("cache directory", StringComparison.OrdinalIgnoreCase)
                                            || me.Contains("simulation", StringComparison.OrdinalIgnoreCase)),
                Is.True);
        });
    }

    private async Task<RenderOptionsData> ReadSceneRenderOptionsAsync(string blendFilePath)
    {
        var blenderDir = RenderTestAssetPaths.ResolveBlenderDir(m_solutionRoot!)
                         ?? throw new InvalidOperationException("No supported Blender prerequisites for current OS/architecture.");
        var blenderRunner = new BlenderRunner(blenderDir, NullLogger.Instance);
        if (!blenderRunner.IsAvailable)
            throw new InvalidOperationException($"Blender not found at {blenderDir}");

        var blenderExecutablePath = typeof(BlenderRunner)
            .GetField("m_blenderPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .GetValue(blenderRunner) as string
            ?? throw new InvalidOperationException("Failed to resolve Blender executable path from BlenderRunner.");

        var scriptPath = Path.Combine(Path.GetTempPath(), $"render_scene_options_{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllLinesAsync(scriptPath,
            [
                "import bpy, json",
                "scene = bpy.context.scene",
                "render = scene.render",
                "cycles = getattr(scene, 'cycles', None)",
                "percentage = max(1, int(render.resolution_percentage))",
                "payload = {",
                "  'resolution_x': int(render.resolution_x * percentage / 100),",
                "  'resolution_y': int(render.resolution_y * percentage / 100),",
                "  'samples': int(getattr(cycles, 'samples', 0) or 0) if cycles is not None else 0,",
                "  'denoise': bool(getattr(cycles, 'use_denoising', False)) if cycles is not None else False",
                "}",
                "print('WIT_DIAGNOSTICS_START')",
                "print(json.dumps(payload))",
                "print('WIT_DIAGNOSTICS_END')"
            ]);

            var args = $"-b \"{blendFilePath}\" --python-exit-code 1 --python \"{scriptPath}\"";

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
                throw new InvalidOperationException($"Blender scene option diagnostics failed with exit code {process.ExitCode}. Stdout: {stdout}\nStderr: {stderr}");

            const string startMarker = "WIT_DIAGNOSTICS_START";
            const string endMarker = "WIT_DIAGNOSTICS_END";
            var startIndex = stdout.IndexOf(startMarker, StringComparison.Ordinal);
            var endIndex = stdout.IndexOf(endMarker, StringComparison.Ordinal);
            if (startIndex < 0 || endIndex <= startIndex)
                throw new InvalidOperationException($"Blender scene option diagnostics markers were not found. Stdout: {stdout}\nStderr: {stderr}");

            var json = stdout.Substring(startIndex + startMarker.Length, endIndex - startIndex - startMarker.Length).Trim();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new RenderOptionsData
            {
                Format = RenderFormat.PNG,
                Engine = RenderEngine.Cycles,
                Samples = root.GetProperty("samples").GetInt32(),
                ResolutionX = root.GetProperty("resolution_x").GetInt32(),
                ResolutionY = root.GetProperty("resolution_y").GetInt32(),
                Denoise = root.GetProperty("denoise").GetBoolean()
            };
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
}
