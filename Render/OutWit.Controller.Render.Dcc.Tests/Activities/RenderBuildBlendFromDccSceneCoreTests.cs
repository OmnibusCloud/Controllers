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
/// Core BuildBlendFromDccScene tests — validate-blend, render still / tiled /
/// frames / video, attachment variants (texture, metallic+roughness, light/
/// sun/spot), plus the three BundledRenderDccScene script tests. Excludes
/// the Tarrasque investigation variants which live in a sibling fixture.
/// </summary>
[TestFixture]
internal sealed class RenderBuildBlendFromDccSceneCoreTests : RenderBuildBlendFromDccSceneTestsBase
{
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
        // CreateValidScene places the mesh node at translation (1, 2, 3) with
        // a horizontal triangle (normal +Z). The default camera fixture lives
        // at (5, -5, 3) — same Z as the triangle — so it ends up edge-on and
        // the triangle is invisible. Reset the mesh node to origin so the
        // camera looks down on the triangle at an angle and actually sees a
        // lit surface. Also add the standard point light (the test's
        // "with point light" wording in the assertion below was aspirational —
        // the original setup had no light at all).
        scene.Nodes[0].LocalTransform.Translation = new DccVector3Data();
        // The triangle in CreateValidScene sits in the local z=0 plane with
        // normal +Z. Rotate the node -90° around X so the triangle stands
        // vertical (XZ plane) with normal +Y — the side that faces the
        // camera positioned at +Y below.
        scene.Nodes[0].LocalTransform.Rotation = new DccQuaternionData
        {
            X = -0.7071067811865476d,
            Y = 0d,
            Z = 0d,
            W = 0.7071067811865476d
        };
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        // The script generator applies CAMERA_LIGHT_LOCAL_AXIS_CORRECTION
        // (a -90° X rotation) to camera + light objects. That correction
        // rotates Blender's default camera forward (-Z) to -Y. So a camera
        // with identity export rotation at (0, +10, 0) ends up looking
        // back along -Y at the origin where the triangle stands.
        var cameraNode = DccRenderTestData.CreateCameraNode();
        cameraNode.LocalTransform.Translation = new DccVector3Data { X = 0d, Y = 10d, Z = 0d };
        cameraNode.LocalTransform.Rotation = new DccQuaternionData { W = 1d };
        scene.Nodes.Add(cameraNode);
        scene.Lights.Add(DccRenderTestData.CreateLight());
        // Move the light to the same +Y side as the camera so it actually
        // illuminates the face the camera sees. The default fixture puts the
        // light at (4, -4, 6) which is fine for the more elaborate test
        // scenes elsewhere but ends up on the wrong side of our minimal
        // single-triangle setup.
        var lightNode = DccRenderTestData.CreateLightNode();
        lightNode.LocalTransform.Translation = new DccVector3Data { X = 3d, Y = 5d, Z = 4d };
        scene.Nodes.Add(lightNode);

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions(256, 256));

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

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found for golden-file validation.");
        RenderGoldenFileAssert.AssertImageMatches(
            storedPath, solutionRoot,
            "BuildBlendFromDccSceneThenRenderStill", RenderEngine.Cycles, 256, 256);
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
}
