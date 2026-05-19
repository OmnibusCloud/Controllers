using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Dcc.Model;
using OutWit.Controller.Render.Dcc.Tests.Mock;
using OutWit.Controller.Render.Dcc.Tests.Utils;
using OutWit.Engine.Sdk;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendFromDccSceneValidateBlendTests
{
    #region Fields

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private RenderTestBlobService m_blobService = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_validate_dcc_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_storageDir))
            Directory.Delete(m_storageDir, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task BuildBlendFromDccSceneThenValidateBlendCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc validate-blend integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndValidate(DccScene:scene)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         String:validation = Render.ValidateBlend(blend);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["validation"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(validation!.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            "Synthetic DCC still render with point light");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        BoostInvestigationLights(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with boosted lights from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        BoostInvestigationLights(scene);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted lights from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithFlatMaterialsAndBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        BoostInvestigationLights(scene);
        SimplifyInvestigationMaterials(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with flat materials and boosted lights from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithIdentityMeshTransformsAndKnownGoodCameraLightThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ResetInvestigationMeshNodeTransformsToIdentity(scene);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        ReplaceInvestigationLightsWithKnownGoodLight(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with identity mesh transforms and known-good camera/light from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithIdentityMeshScaleAndKnownGoodCameraLightThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ResetInvestigationMeshNodeScalesToIdentity(scene);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        ReplaceInvestigationLightsWithKnownGoodLight(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with identity mesh scale and known-good camera/light from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndKnownGoodLightDataOnExportedLightNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        ReplaceInvestigationLightDataKeepingFirstExportedLightNode(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and known-good light data on exported light node from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithExportedCameraAndKnownGoodLightThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ReplaceInvestigationLightsWithKnownGoodLight(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with exported camera and known-good light from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithScaledExportedCameraAndLightTranslationsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ScaleInvestigationNonMeshTranslations(scene, INVESTIGATION_NON_MESH_TRANSLATION_SCALE);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with scaled exported camera/light translations from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithExportedCameraTranslationAndKnownGoodRotationThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ReplaceInvestigationLightsWithKnownGoodLight(scene);
        ReplaceInvestigationCameraRotationKeepingExportedTranslation(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with exported camera translation and known-good rotation from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndScaledExportedLightTranslationThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        ReplaceInvestigationLightDataKeepingFirstExportedLightNode(scene);
        ScaleFirstExportedLightNodeTranslation(scene, INVESTIGATION_NON_MESH_TRANSLATION_SCALE);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and scaled exported light translation from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithScaledExportedCameraTranslationAndKnownGoodRotationThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ReplaceInvestigationLightsWithKnownGoodLight(scene);
        ReplaceInvestigationCameraRotationKeepingExportedTranslation(scene);
        ScaleFirstExportedCameraTranslation(scene, INVESTIGATION_NON_MESH_TRANSLATION_SCALE);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with scaled exported camera translation and known-good rotation from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedPointLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        BoostInvestigationLights(scene);
        ConvertInvestigationLightsToPoint(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted point lights from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedExportedLightDataOnKnownGoodLightNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        BoostInvestigationLights(scene);
        ReplaceInvestigationLightNodeKeepingFirstExportedLightData(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted exported light data on known-good light node from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedExportedPointLightDataOnKnownGoodLightNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        BoostInvestigationLights(scene);
        ReplaceInvestigationLightNodeKeepingFirstExportedLightData(scene);
        ConvertInvestigationLightsToPoint(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted exported point light data on known-good light node from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithoutTexturesAndWithBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        RemoveInvestigationTexturesKeepMaterialScalars(scene);
        BoostInvestigationLights(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render without textures and with boosted lights from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndLightButOriginalTexturesThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        ReplaceInvestigationLightsWithKnownGoodLight(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera/light but original textures from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedFirstExportedPointLightOnExportedNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        SimplifyInvestigationMaterials(scene);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        KeepOnlyFirstExportedLight(scene);
        BoostInvestigationLights(scene);
        ConvertInvestigationLightsToPoint(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted first exported point light on exported node from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedFirstExportedPointLightOnExportedNodeWithOriginalTexturesThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        KeepOnlyFirstExportedLight(scene);
        BoostInvestigationLights(scene);
        ConvertInvestigationLightsToPoint(scene);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted first exported point light on exported node with original textures from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedWideSpotLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        BoostInvestigationLights(scene);
        WidenInvestigationSpotLights(scene, 120d);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and boosted wide spot lights from '{sceneJsonPath}'");
    }

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Publish\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndVeryStrongLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Publish\\Temp\\candidate_validate_smoke.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var scene = await LoadSceneFromJsonAsync(sceneJsonPath);
        NormalizeInvestigationScenePaths(scene, solutionRoot);
        ReplaceInvestigationCameraWithKnownGoodCamera(scene);
        SetInvestigationLightIntensityFloor(scene, 100000d);
        SetInvestigationLightRangeFloor(scene, 100d);

        var script = """
                     Job:BuildAndRender(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var frame = scene.RenderSettings?.FrameStart > 0 ? scene.RenderSettings.FrameStart : 1;
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, frame, CreateInvestigationRenderOptions(scene));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });

        AssertImageContainsMeaningfullyLitPixels(storedPath,
            $"Local exported Tarrasque render with known-good camera and very strong lights from '{sceneJsonPath}'");
    }

    [Test]
    public async Task BuildBlendFromDccSceneThenRenderStillTiledCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc tiled still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderTiled(DccScene:scene, Int:frame, Int:tilesX, Int:tilesY, RenderOptions:options, TileOptions:tileOptions)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.SplitTiles(blend, frame, tilesX, tilesY, options, tileOptions);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectTiles(rendered, options, tileOptions);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 2, 2, CreateRenderOptions(), CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BundledRenderDccSceneStillTiledScriptCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for bundled RenderDccSceneStillTiled integration test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");
        var scriptPath = Path.Combine(solutionRoot, "@Scripts", "Debug", "RenderDccSceneStillTiled.wit");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"Bundled script was not found at {scriptPath}");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = await File.ReadAllTextAsync(scriptPath);
        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 2, 2, CreateRenderOptions(), CreateTileOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BundledRenderDccSceneFramesScriptCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for bundled RenderDccSceneFrames integration test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");
        var scriptPath = Path.Combine(solutionRoot, "@Scripts", "Debug", "RenderDccSceneFrames.wit");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"Bundled script was not found at {scriptPath}");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = await File.ReadAllTextAsync(scriptPath);
        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.RenderSettings.FrameEnd = 3;

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 3, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobIds = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(resultBlobIds, Is.Not.Null);
        Assert.That(resultBlobIds!, Has.Count.EqualTo(3));

        foreach (var resultBlobId in resultBlobIds)
        {
            Assert.That(resultBlobId, Is.Not.Null);
            var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(storedPath), Is.True);
                Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
                Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
            });
        }
    }

    [Test]
    public async Task BundledRenderDccSceneVideoScriptCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for bundled RenderDccSceneVideo integration test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");
        var scriptPath = Path.Combine(solutionRoot, "@Scripts", "Debug", "RenderDccSceneVideo.wit");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"Bundled script was not found at {scriptPath}");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = await File.ReadAllTextAsync(scriptPath);
        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.RenderSettings.FrameEnd = 3;

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 3, CreateRenderOptions(), CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".mp4"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneThenRenderFramesCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc frame-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderFrames(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         BlobCollection:result = Render.Collect(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.RenderSettings.FrameEnd = 3;

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 3, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobIds = job.Variables["result"].Value as IReadOnlyList<Guid?>;
        Assert.That(resultBlobIds, Is.Not.Null);
        Assert.That(resultBlobIds!, Has.Count.EqualTo(3));

        foreach (var resultBlobId in resultBlobIds)
        {
            Assert.That(resultBlobId, Is.Not.Null);

            var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(storedPath), Is.True);
                Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
                Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
            });
        }
    }

    [Test]
    public async Task BuildBlendFromDccSceneThenRenderVideoCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc video-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderVideo(DccScene:scene, Int:startFrame, Int:endFrame, RenderOptions:options, VideoOptions:video)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, startFrame, endFrame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         BlobCollection:frames = Render.Collect(rendered, options);
                         Blob:result = Render.EncodeVideo(frames, video);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.RenderSettings.FrameEnd = 3;

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, 3, CreateRenderOptions(), CreateVideoOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".mp4"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithTextureAttachmentThenValidateBlendCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc textured validate-blend integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndValidateTextured(DccScene:scene)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         String:validation = Render.ValidateBlend(blend);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var texturePath = Path.Combine(m_storageDir, "albedo.png");
        File.WriteAllBytes(texturePath, Convert.FromBase64String(MINIMAL_PNG_BASE64));
        var textureBlobId = m_blobService.RegisterExistingFile(texturePath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(textureBlobId));

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var validationJson = job.Variables["validation"].Value as string;
        var validation = validationJson == null ? null : JsonSerializer.Deserialize<RenderValidateBlendData>(validationJson, JSON_OPTIONS);
        Assert.That(validation, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(validation!.IsValid, Is.True);
            Assert.That(validation.Issues, Is.Empty);
            Assert.That(validation.Warnings.Any(me => me.Contains("image asset", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithTextureAttachmentThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc textured still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderTexturedStill(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());

        var texturePath = Path.Combine(m_storageDir, "albedo.png");
        File.WriteAllBytes(texturePath, Convert.FromBase64String(MINIMAL_PNG_BASE64));
        var textureBlobId = m_blobService.RegisterExistingFile(texturePath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(textureBlobId));

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithMetallicAndRoughnessTextureAttachmentsThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc PBR-textured still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderPbrTexturedStill(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.ImageAssets.Add(DccRenderTestData.CreateMetallicImageAsset());
        scene.ImageAssets.Add(DccRenderTestData.CreateRoughnessImageAsset());
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Metallic,
            ImageAssetId = "image:metallic"
        });
        scene.Materials[0].TextureSlots.Add(new DccTextureSlotData
        {
            Slot = DccTextureSlotKind.Roughness,
            ImageAssetId = "image:roughness"
        });

        var albedoPath = Path.Combine(m_storageDir, "albedo.png");
        var metallicPath = Path.Combine(m_storageDir, "metallic.png");
        var roughnessPath = Path.Combine(m_storageDir, "roughness.png");
        var pngBytes = Convert.FromBase64String(MINIMAL_PNG_BASE64);
        File.WriteAllBytes(albedoPath, pngBytes);
        File.WriteAllBytes(metallicPath, pngBytes);
        File.WriteAllBytes(roughnessPath, pngBytes);

        var albedoBlobId = m_blobService.RegisterExistingFile(albedoPath);
        var metallicBlobId = m_blobService.RegisterExistingFile(metallicPath);
        var roughnessBlobId = m_blobService.RegisterExistingFile(roughnessPath);
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(albedoBlobId));
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(metallicBlobId, "C:/textures/metallic.png", "textures/metallic.png"));
        scene.AttachedFiles.Add(DccRenderTestData.CreateImageAttachment(roughnessBlobId, "C:/textures/roughness.png", "textures/roughness.png"));

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithLightThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc lighted still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderLightedStill(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithSunLightThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc sun-light still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderSunLightStill(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Lights.Add(DccRenderTestData.CreateSunLight());
        scene.Nodes.Add(DccRenderTestData.CreateSunLightNode());

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithSpotLightThenRenderStillCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc spot-light still-render integration test.");

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        WitEngineSdk.Instance.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new RenderDccTestNodesManager(WitEngineNodeSdk.Instance));
            });
        var hostEngine = WitEngineSdk.Instance;

        var script = """
                     Job:BuildAndRenderSpotLightStill(DccScene:scene, Int:frame, RenderOptions:options)
                     {
                         Blob:blend = Render.BuildBlendFromDccScene(scene);
                         RenderTaskCollection:tasks = Render.Split(blend, frame, frame, options);
                         RenderResultCollection:rendered = Grid.ForEach(task in tasks)
                             => Render.Frame(task);
                         Blob:result = Render.CollectStill(rendered, options);
                     }
                     """;

        var job = hostEngine.Compile(script);
        var scene = DccRenderTestData.CreateValidScene();
        scene.Materials[0].TextureSlots.Clear();
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
        scene.Lights.Add(DccRenderTestData.CreateSpotLight());
        scene.Nodes.Add(DccRenderTestData.CreateSpotLightNode());

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".png"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        });
    }

    #endregion

    #region Constants

    private const string MINIMAL_PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    private const double INVESTIGATION_NON_MESH_TRANSLATION_SCALE = 0.1048218d;

    #endregion

    #region Helpers

    private static RenderOptionsData CreateRenderOptions()
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = RenderEngine.Cycles,
            Samples = 4,
            ResolutionX = 64,
            ResolutionY = 64,
            Denoise = false
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

    private static TileOptionsData CreateTileOptions()
    {
        return new TileOptionsData
        {
            OverlapPx = 8,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }

    private static RenderOptionsData CreateInvestigationRenderOptions(DccSceneData scene)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = scene.RenderSettings?.TargetEngine ?? RenderEngine.Cycles,
            Samples = scene.RenderSettings?.Samples ?? 64,
            ResolutionX = scene.RenderSettings?.ResolutionX ?? 1920,
            ResolutionY = scene.RenderSettings?.ResolutionY ?? 1080,
            Denoise = true
        };
    }

    private static async Task<DccSceneData> LoadSceneFromJsonAsync(string sceneJsonPath)
    {
        var json = await File.ReadAllTextAsync(sceneJsonPath);
        return JsonSerializer.Deserialize<DccSceneData>(json, JSON_OPTIONS)
               ?? throw new InvalidOperationException($"Failed to deserialize DCC scene from '{sceneJsonPath}'.");
    }

    private static string? FindLatestExportedDccSceneJsonPath(string solutionRoot, string sceneName)
    {
        var candidateRoot = Path.Combine(solutionRoot, "@Publish", "Temp", "candidate_validate_smoke");
        if (!Directory.Exists(candidateRoot))
            return null;

        return Directory
            .EnumerateDirectories(candidateRoot, sceneName + "_*", SearchOption.TopDirectoryOnly)
            .Select(me => new DirectoryInfo(me))
            .OrderByDescending(me => me.LastWriteTimeUtc)
            .Select(me => Path.Combine(me.FullName, "output", "dcc-scene.json"))
            .FirstOrDefault(File.Exists);
    }

    private static void AssertImageContainsMeaningfullyLitPixels(string imagePath, string context)
    {
        using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(imagePath);

        long meaningfullyLitPixels = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R >= 8 || pixel.G >= 8 || pixel.B >= 8)
                    meaningfullyLitPixels++;
            }
        }

        Assert.That(meaningfullyLitPixels, Is.GreaterThan(0),
            $"{context}: rendered image contains only near-black pixels and is not visually meaningful.");
    }

    private static void NormalizeInvestigationScenePaths(DccSceneData scene, string solutionRoot)
    {
        var dataRoot = Path.Combine(solutionRoot, "@Data", "3ds_max");
        foreach (var imageAsset in scene.ImageAssets)
        {
            imageAsset.RelativePath = string.Empty;

            if (Path.IsPathRooted(imageAsset.SourcePath) || string.IsNullOrWhiteSpace(imageAsset.SourcePath))
                continue;

            var normalizedPath = Path.Combine(dataRoot, imageAsset.SourcePath);
            if (File.Exists(normalizedPath))
                imageAsset.SourcePath = normalizedPath;
        }
    }

    private static void BoostInvestigationLights(DccSceneData scene)
    {
        foreach (var light in scene.Lights)
        {
            light.Intensity = Math.Max(light.Intensity, 1000d);

            if (light.Kind is DccLightKind.Point or DccLightKind.Spot)
                light.Range = Math.Max(light.Range, 25d);
        }
    }

    private static void ReplaceInvestigationCameraWithKnownGoodCamera(DccSceneData scene)
    {
        scene.Cameras.Clear();
        scene.Nodes.RemoveAll(me => me.Kind == DccNodeKind.Camera);
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        scene.Nodes.Add(DccRenderTestData.CreateCameraNode());
    }

    private static void ReplaceInvestigationCameraRotationKeepingExportedTranslation(DccSceneData scene)
    {
        var exportedCameraNode = scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Camera)
                                 ?? throw new InvalidOperationException("Expected an exported camera node for the investigation scene.");
        var knownGoodCameraNode = DccRenderTestData.CreateCameraNode();
        exportedCameraNode.LocalTransform.Rotation = knownGoodCameraNode.LocalTransform.Rotation;
        exportedCameraNode.LocalTransform.Scale = knownGoodCameraNode.LocalTransform.Scale;
    }

    private static void ReplaceInvestigationLightsWithKnownGoodLight(DccSceneData scene)
    {
        scene.Lights.Clear();
        scene.Nodes.RemoveAll(me => me.Kind == DccNodeKind.Light);
        scene.Lights.Add(DccRenderTestData.CreateLight());
        scene.Nodes.Add(DccRenderTestData.CreateLightNode());
    }

    private static void ReplaceInvestigationLightDataKeepingFirstExportedLightNode(DccSceneData scene)
    {
        var firstLightNode = scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Light)
                             ?? throw new InvalidOperationException("Expected at least one exported light node for the investigation scene.");

        scene.Nodes.RemoveAll(me => me.Kind == DccNodeKind.Light && !ReferenceEquals(me, firstLightNode));
        var light = DccRenderTestData.CreateLight();
        firstLightNode.LightId = light.Id;
        scene.Lights.Clear();
        scene.Lights.Add(light);
    }

    private static void ReplaceInvestigationLightNodeKeepingFirstExportedLightData(DccSceneData scene)
    {
        var exportedLight = scene.Lights.FirstOrDefault()
                            ?? throw new InvalidOperationException("Expected at least one exported light for the investigation scene.");

        scene.Nodes.RemoveAll(me => me.Kind == DccNodeKind.Light);
        var knownGoodLightNode = DccRenderTestData.CreateLightNode();
        knownGoodLightNode.LightId = exportedLight.Id;
        scene.Nodes.Add(knownGoodLightNode);
        scene.Lights.Clear();
        scene.Lights.Add(exportedLight);
    }

    private static void ResetInvestigationMeshNodeTransformsToIdentity(DccSceneData scene)
    {
        foreach (var node in scene.Nodes.Where(me => me.Kind == DccNodeKind.Mesh))
        {
            node.LocalTransform = new DccTransformData
            {
                Translation = new DccVector3Data(),
                Rotation = new DccQuaternionData { W = 1d },
                Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d }
            };
        }
    }

    private static void ResetInvestigationMeshNodeScalesToIdentity(DccSceneData scene)
    {
        foreach (var node in scene.Nodes.Where(me => me.Kind == DccNodeKind.Mesh))
        {
            node.LocalTransform.Scale = new DccVector3Data { X = 1d, Y = 1d, Z = 1d };
        }
    }

    private static void SimplifyInvestigationMaterials(DccSceneData scene)
    {
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();

        foreach (var material in scene.Materials)
        {
            material.BaseColor = new DccColorData { R = 0.85d, G = 0.85d, B = 0.85d, A = 1d };
            material.TextureSlots.Clear();
            material.Opacity = 1d;
            material.AlphaMode = DccMaterialAlphaMode.Blend;
            material.Metallic = 0d;
            material.Roughness = 0.5d;
            material.NormalStrength = 1d;
        }
    }

    private static void RemoveInvestigationTexturesKeepMaterialScalars(DccSceneData scene)
    {
        scene.ImageAssets.Clear();
        scene.AttachedFiles.Clear();

        foreach (var material in scene.Materials)
            material.TextureSlots.Clear();
    }

    private static void ScaleInvestigationNonMeshTranslations(DccSceneData scene, double factor)
    {
        foreach (var node in scene.Nodes.Where(me => me.Kind != DccNodeKind.Mesh))
        {
            node.LocalTransform.Translation = new DccVector3Data
            {
                X = node.LocalTransform.Translation.X * factor,
                Y = node.LocalTransform.Translation.Y * factor,
                Z = node.LocalTransform.Translation.Z * factor
            };
        }
    }

    private static void ScaleFirstExportedLightNodeTranslation(DccSceneData scene, double factor)
    {
        var lightNode = scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Light)
                        ?? throw new InvalidOperationException("Expected an exported light node for the investigation scene.");
        lightNode.LocalTransform.Translation = new DccVector3Data
        {
            X = lightNode.LocalTransform.Translation.X * factor,
            Y = lightNode.LocalTransform.Translation.Y * factor,
            Z = lightNode.LocalTransform.Translation.Z * factor
        };
    }

    private static void ScaleFirstExportedCameraTranslation(DccSceneData scene, double factor)
    {
        var cameraNode = scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Camera)
                         ?? throw new InvalidOperationException("Expected an exported camera node for the investigation scene.");
        cameraNode.LocalTransform.Translation = new DccVector3Data
        {
            X = cameraNode.LocalTransform.Translation.X * factor,
            Y = cameraNode.LocalTransform.Translation.Y * factor,
            Z = cameraNode.LocalTransform.Translation.Z * factor
        };
    }

    private static void ConvertInvestigationLightsToPoint(DccSceneData scene)
    {
        foreach (var light in scene.Lights)
        {
            light.Kind = DccLightKind.Point;
            light.SpotAngleDegrees = 45d;
        }
    }

    private static void KeepOnlyFirstExportedLight(DccSceneData scene)
    {
        var firstLightNode = scene.Nodes.FirstOrDefault(me => me.Kind == DccNodeKind.Light)
                             ?? throw new InvalidOperationException("Expected at least one exported light node for the investigation scene.");
        var firstLight = scene.Lights.FirstOrDefault(me => me.Id == firstLightNode.LightId)
                         ?? throw new InvalidOperationException("Expected the first exported light node to reference an exported light.");

        scene.Nodes.RemoveAll(me => me.Kind == DccNodeKind.Light && !ReferenceEquals(me, firstLightNode));
        scene.Lights.Clear();
        scene.Lights.Add(firstLight);
    }

    private static void WidenInvestigationSpotLights(DccSceneData scene, double spotAngleDegrees)
    {
        foreach (var light in scene.Lights.Where(me => me.Kind == DccLightKind.Spot))
            light.SpotAngleDegrees = spotAngleDegrees;
    }

    private static void SetInvestigationLightIntensityFloor(DccSceneData scene, double intensity)
    {
        foreach (var light in scene.Lights)
            light.Intensity = Math.Max(light.Intensity, intensity);
    }

    private static void SetInvestigationLightRangeFloor(DccSceneData scene, double range)
    {
        foreach (var light in scene.Lights.Where(me => me.Kind is DccLightKind.Point or DccLightKind.Spot))
            light.Range = Math.Max(light.Range, range);
    }

    #endregion
}
