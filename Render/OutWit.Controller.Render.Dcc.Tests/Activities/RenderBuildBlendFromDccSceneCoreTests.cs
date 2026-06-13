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
    public async Task BuildBlendFromDccSceneThenRenderStillWithGlassMaterialCompletesTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc glass still-render integration test.");

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
        // Make the material refractive glass — exercises the Blender 5.1 'Transmission Weight'
        // and 'IOR' Principled BSDF sockets end-to-end (a wrong socket name would make
        // BuildBlendFromDccScene fail in Blender, failing this test).
        scene.Materials[0].Transmission = 1d;
        scene.Materials[0].Ior = 1.5d;

        // Same single-triangle framing as the opaque still test.
        scene.Nodes[0].LocalTransform.Translation = new DccVector3Data();
        scene.Nodes[0].LocalTransform.Rotation = new DccQuaternionData
        {
            X = -0.7071067811865476d,
            Y = 0d,
            Z = 0d,
            W = 0.7071067811865476d
        };
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        var cameraNode = DccRenderTestData.CreateCameraNode();
        cameraNode.LocalTransform.Translation = new DccVector3Data { X = 0d, Y = 10d, Z = 0d };
        cameraNode.LocalTransform.Rotation = new DccQuaternionData { W = 1d };
        scene.Nodes.Add(cameraNode);
        scene.Lights.Add(DccRenderTestData.CreateLight());
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
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithCameraDofAreaLightColorAndSecondUvRendersTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc camera-DOF/area-light/color/UV integration test.");

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

        // Exercise every new Blender 5.1 API path at once: a second UV set, color management,
        // an AREA light with a shadow toggle, and a depth-of-field camera. A wrong socket/attribute
        // name in any of these fails the .blend build in Blender, failing this test.
        scene.Meshes[0].Uv1 = [.. scene.Meshes[0].Uv0.Select(uv => new DccVector2Data { X = uv.X, Y = uv.Y })];
        scene.RenderSettings.ViewTransform = "Standard";
        scene.RenderSettings.Exposure = 0.5d;

        scene.Nodes[0].LocalTransform.Translation = new DccVector3Data();
        scene.Nodes[0].LocalTransform.Rotation = new DccQuaternionData
        {
            X = -0.7071067811865476d,
            Y = 0d,
            Z = 0d,
            W = 0.7071067811865476d
        };

        var camera = DccRenderTestData.CreateCamera();
        camera.EnableDepthOfField = true;
        camera.FocusDistance = 10d;
        camera.FStop = 2.0d;
        scene.Cameras.Add(camera);
        var cameraNode = DccRenderTestData.CreateCameraNode();
        cameraNode.LocalTransform.Translation = new DccVector3Data { X = 0d, Y = 10d, Z = 0d };
        cameraNode.LocalTransform.Rotation = new DccQuaternionData { W = 1d };
        scene.Nodes.Add(cameraNode);

        var light = DccRenderTestData.CreateLight();
        light.Kind = DccLightKind.Area;
        light.AreaWidth = 4d;
        light.AreaHeight = 4d;
        light.CastShadows = true;
        scene.Lights.Add(light);
        var lightNode = DccRenderTestData.CreateLightNode();
        lightNode.LocalTransform.Translation = new DccVector3Data { X = 3d, Y = 5d, Z = 4d };
        scene.Nodes.Add(lightNode);

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions(256, 256));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);
        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        AssertImageContainsMeaningfullyLitPixels(storedPath, "camera-DOF/area-light/color/second-UV render");
    }

    [Test]
    public async Task BuildBlendFromDccSceneWithWorldEnvironmentLightsSceneWithoutLampsTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for RenderDcc world-environment integration test.");

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

        // Bright world, and NO explicit lights — the only illumination is the world environment.
        // A non-black render proves the world emits ambient light (so transmissive/reflective
        // materials have an environment to refract/reflect).
        scene.World = new DccWorldData
        {
            BackgroundColor = new DccColorData { R = 0.8d, G = 0.8d, B = 0.85d, A = 1d },
            Strength = 2d
        };

        scene.Nodes[0].LocalTransform.Translation = new DccVector3Data();
        scene.Nodes[0].LocalTransform.Rotation = new DccQuaternionData
        {
            X = -0.7071067811865476d,
            Y = 0d,
            Z = 0d,
            W = 0.7071067811865476d
        };
        scene.Cameras.Add(DccRenderTestData.CreateCamera());
        var cameraNode = DccRenderTestData.CreateCameraNode();
        cameraNode.LocalTransform.Translation = new DccVector3Data { X = 0d, Y = 10d, Z = 0d };
        cameraNode.LocalTransform.Rotation = new DccQuaternionData { W = 1d };
        scene.Nodes.Add(cameraNode);

        var status = await hostEngine.ScheduleAndWaitAsync(job, scene, 1, CreateRenderOptions(256, 256));

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.That(File.Exists(storedPath), Is.True);
        Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(0));
        // The only light is the world environment — a lit result proves the world emits ambient.
        AssertImageContainsMeaningfullyLitPixels(storedPath, "world-environment-only render");
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
    public async Task BundledRenderDccSceneExportBlendScriptReturnsPackedBlendTest()
    {
        if (RenderTestAssetPaths.FindRenderBlenderRoot() == null)
            Assert.Ignore("Packaged Blender runtime not found for bundled RenderDccSceneExportBlend integration test.");

        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new DirectoryNotFoundException("Solution root not found.");
        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");
        var scriptPath = Path.Combine(solutionRoot, "@Scripts", "Debug", "RenderDccSceneExportBlend.wit");
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

        // Export builds the .blend and returns its blob - no render activities run.
        var status = await hostEngine.ScheduleAndWaitAsync(job, scene);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var resultBlobId = (Guid?)job.Variables["result"].Value;
        Assert.That(resultBlobId, Is.Not.Null);

        var storedPath = m_blobService.GetStoredPath(resultBlobId!.Value);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(storedPath), Is.True);
            Assert.That(Path.GetExtension(storedPath), Is.EqualTo(".blend"));
            Assert.That(new FileInfo(storedPath).Length, Is.GreaterThan(10000));
        });

        // Confirm a genuine .blend, not a stub: an uncompressed .blend starts with the "BLENDER"
        // magic; Blender may also save it compressed (gzip or zstd), so accept those signatures too.
        await using var blendStream = File.OpenRead(storedPath);
        var header = new byte[7];
        Assert.That(blendStream.Read(header, 0, header.Length), Is.EqualTo(header.Length));

        var isUncompressed = System.Text.Encoding.ASCII.GetString(header) == "BLENDER";
        var isGzip = header[0] == 0x1F && header[1] == 0x8B;
        var isZstd = header[0] == 0x28 && header[1] == 0xB5 && header[2] == 0x2F && header[3] == 0xFD;
        Assert.That(isUncompressed || isGzip || isZstd, Is.True,
            $"Result is not a recognized .blend signature (first bytes: {BitConverter.ToString(header)}).");
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
