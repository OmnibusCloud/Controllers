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

/// <summary>
/// Tarrasque DCC-scene investigation tests — 21 progressive variations on a
/// real exported Tarrasque scene that probe what makes a meaningful render:
/// camera substitution, light boosting, mesh transform identity, texture
/// removal, scaled translations, point vs spot vs sun lights, etc. All these
/// are <c>[Explicit]</c>/conditional (gated on @Output artefacts) and skip
/// when those artefacts are absent. Owns the Investigation* scene-mutation
/// helpers since they exist only to support these experiments.
/// </summary>
[TestFixture]
internal sealed class RenderBuildBlendFromDccSceneTarrasqueInvestigationTests : RenderBuildBlendFromDccSceneTestsBase
{
    #region Constants

    private const double INVESTIGATION_NON_MESH_TRANSLATION_SCALE = 0.1048218d;

    #endregion

    #region Tests

    [Test]
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithFlatMaterialsAndBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithIdentityMeshTransformsAndKnownGoodCameraLightThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithIdentityMeshScaleAndKnownGoodCameraLightThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndKnownGoodLightDataOnExportedLightNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithExportedCameraAndKnownGoodLightThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithScaledExportedCameraAndLightTranslationsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithExportedCameraTranslationAndKnownGoodRotationThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndScaledExportedLightTranslationThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithScaledExportedCameraTranslationAndKnownGoodRotationThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedPointLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedExportedLightDataOnKnownGoodLightNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedExportedPointLightDataOnKnownGoodLightNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithoutTexturesAndWithBoostedLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndLightButOriginalTexturesThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedFirstExportedPointLightOnExportedNodeThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedFirstExportedPointLightOnExportedNodeWithOriginalTexturesThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndBoostedWideSpotLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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
    [Explicit("Requires a previously exported local realistic 3ds Max dcc-scene.json under @Output\\Temp\\candidate_validate_smoke and packaged Blender runtime.")]
    public async Task ExportedTarrasqueDccSceneJsonWithKnownGoodCameraAndVeryStrongLightsThenRenderStillLocallyProducesMeaningfullyLitImageTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for local exported 3ds Max DCC render investigation test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var sceneJsonPath = FindLatestExportedDccSceneJsonPath(solutionRoot, "TarrasqueTextured");
        if (sceneJsonPath == null)
            Assert.Ignore("No exported TarrasqueTextured dcc-scene.json was found under @Output\\Temp\\candidate_validate_smoke.");

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


    #endregion

    #region Investigation Helpers

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
