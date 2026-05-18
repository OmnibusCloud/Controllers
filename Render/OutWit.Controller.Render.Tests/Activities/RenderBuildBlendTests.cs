using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Tests.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Render.Tests.Activities;

[TestFixture]
public sealed class RenderBuildBlendTests
{
    #region Fields

    private RenderTestBlobService m_blobService = null!;
    private IWitEngine m_engine = null!;
    private string m_storageDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_storageDir = Path.Combine(Path.GetTempPath(), $"witcloud_render_buildblend_test_{Guid.NewGuid():N}");
        m_blobService = new RenderTestBlobService(m_storageDir);

        var controllersPath = RenderTestAssetPaths.FindControllersPath()
                              ?? throw new DirectoryNotFoundException("@Controllers directory not found");

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
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
    public void ParseRenderBuildBlendActivityTest()
    {
        var script = """
                     Job:Build(RenderScene:scene)
                     {
                         Blob:blend = Render.BuildBlend(scene);
                     }
                     """;

        var job = m_engine.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["blend"], Is.Not.Null);
    }

    [Test]
    public void ParseRenderBuildBlendFromRefsActivityTest()
    {
        var script = """
                     Job:Build(RenderSceneRef:scene)
                     {
                         Blob:blend = Render.BuildBlendFromRefs(scene);
                     }
                     """;

        var job = m_engine.Compile(script);

        Assert.That(job.Activities.Count, Is.EqualTo(1));
        Assert.That(job.Variables["blend"], Is.Not.Null);
    }

    [Test]
    public async Task BuildBlendUploadsInlineBlendPayloadTest()
    {
        var script = """
                     Job:Build(RenderScene:scene)
                     {
                         Blob:blend = Render.BuildBlend(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderSceneData
            {
                FileName = "inline.blend",
                BlendFileBytes = payload
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var blobId = (Guid?)job.Variables["blend"].Value;
        Assert.That(blobId, Is.Not.Null);
        Assert.That(File.ReadAllBytes(m_blobService.GetStoredPath(blobId!.Value)), Is.EqualTo(payload));
    }

    [Test]
    public async Task BuildBlendFromRefsReturnsExistingBlendBlobTest()
    {
        var script = """
                     Job:Build(RenderSceneRef:scene)
                     {
                         Blob:blend = Render.BuildBlendFromRefs(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var blendPath = Path.Combine(m_storageDir, "existing.blend");
        var payload = new byte[] { 9, 8, 7, 6 };
        File.WriteAllBytes(blendPath, payload);
        var sourceBlobId = m_blobService.RegisterExistingFile(blendPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderSceneRefData
            {
                BlendBlobId = sourceBlobId
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        Assert.That(job.Variables["blend"].Value, Is.EqualTo(sourceBlobId));
    }

    [Test]
    public async Task BuildBlendFromRefsPersistsAttachmentManifestNextToLocalBlendTest()
    {
        var script = """
                     Job:Build(RenderSceneRef:scene)
                     {
                         Blob:blend = Render.BuildBlendFromRefs(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var blendPath = Path.Combine(m_storageDir, "existing-with-attachments.blend");
        File.WriteAllBytes(blendPath, [9, 8, 7, 6]);
        var sourceBlobId = m_blobService.RegisterExistingFile(blendPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderSceneRefData
            {
                BlendBlobId = sourceBlobId,
                AttachedFiles =
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Font",
                        OriginalPath = "C:/Assets/Fonts/Brand.ttf",
                        RelativePath = "deps/fonts/Brand.ttf",
                        PackagingStrategy = "ScenePackageZip"
                    }
                ]
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");
        var outputBlobId = (Guid)job.Variables["blend"].Value!;
        Assert.That(outputBlobId, Is.Not.EqualTo(sourceBlobId));

        var manifestPath = m_blobService.GetStoredPath(outputBlobId) + ".attachments.json";
        Assert.That(File.Exists(manifestPath), Is.True);
        var manifestJson = await File.ReadAllTextAsync(manifestPath);
        Assert.That(manifestJson, Does.Contain("Brand.ttf"));
        Assert.That(manifestJson, Does.Contain("ScenePackageZip"));
    }

    [Test]
    public async Task BuildBlendFromRefsMaterializesSceneAttachmentBlobNextToLocalBlendTest()
    {
        var script = """
                     Job:Build(RenderSceneRef:scene)
                     {
                         Blob:blend = Render.BuildBlendFromRefs(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var sourceBlendPath = RenderTestAssetPaths.GetTestScenePath(solutionRoot);
        if (!File.Exists(sourceBlendPath))
            Assert.Ignore($"Test scene not found at {sourceBlendPath}");

        var blendPath = Path.Combine(m_storageDir, "existing-with-font-attachment.blend");
        File.Copy(sourceBlendPath, blendPath, overwrite: true);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);

        var fontPath = Path.Combine(m_storageDir, "Brand.ttf");
        File.WriteAllBytes(fontPath, [1, 2, 3, 4]);
        var fontBlobId = m_blobService.RegisterExistingFile(fontPath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderSceneRefData
            {
                BlendBlobId = sceneBlobId,
                AttachedFiles =
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Font",
                        BlobId = fontBlobId,
                        OriginalPath = "C:/Assets/Fonts/Brand.ttf",
                        RelativePath = "deps/fonts/Brand.ttf",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var outputBlobId = (Guid)job.Variables["blend"].Value!;
        Assert.That(outputBlobId, Is.Not.EqualTo(sceneBlobId));

        var outputBlendPath = m_blobService.GetStoredPath(outputBlobId);
        var materializedFontPath = Path.Combine(Path.GetDirectoryName(outputBlendPath)!, "deps", "fonts", "Brand.ttf");
        Assert.That(File.Exists(materializedFontPath), Is.True);
        Assert.That(File.ReadAllBytes(materializedFontPath), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public async Task BuildBlendFromRefsMaterializesLinkedLibraryAndVolumeSceneAttachmentBlobsNextToLocalBlendTest()
    {
        var script = """
                     Job:Build(RenderSceneRef:scene)
                     {
                         Blob:blend = Render.BuildBlendFromRefs(scene);
                     }
                     """;

        var job = m_engine.Compile(script);
        var solutionRoot = RenderTestAssetPaths.FindSolutionRoot()
                           ?? throw new InvalidOperationException("Solution root not found.");
        var sourceBlendPath = RenderTestAssetPaths.GetTestScenePath(solutionRoot);
        if (!File.Exists(sourceBlendPath))
            Assert.Ignore($"Test scene not found at {sourceBlendPath}");

        var blendPath = Path.Combine(m_storageDir, "existing-with-library-volume-attachments.blend");
        File.Copy(sourceBlendPath, blendPath, overwrite: true);
        var sceneBlobId = m_blobService.RegisterExistingFile(blendPath);

        var libraryPath = Path.Combine(m_storageDir, "library.blend");
        File.WriteAllBytes(libraryPath, [1, 2, 3, 4]);
        var libraryBlobId = m_blobService.RegisterExistingFile(libraryPath);

        var volumePath = Path.Combine(m_storageDir, "cloud.vdb");
        File.WriteAllBytes(volumePath, [5, 6, 7, 8]);
        var volumeBlobId = m_blobService.RegisterExistingFile(volumePath);

        var status = await m_engine.ScheduleAndWaitAsync(
            job,
            new RenderSceneRefData
            {
                BlendBlobId = sceneBlobId,
                AttachedFiles =
                [
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "LinkedLibrary",
                        BlobId = libraryBlobId,
                        OriginalPath = "C:/Assets/Libraries/library.blend",
                        RelativePath = "deps/linked-libraries/library.blend",
                        PackagingStrategy = "SceneAttachmentBlob"
                    },
                    new RenderSceneAttachmentRefData
                    {
                        Kind = "Volume",
                        BlobId = volumeBlobId,
                        OriginalPath = "C:/Assets/Volumes/cloud.vdb",
                        RelativePath = "deps/volumes/cloud.vdb",
                        PackagingStrategy = "SceneAttachmentBlob"
                    }
                ]
            });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), $"Job failed: {status.Message}");

        var outputBlobId = (Guid)job.Variables["blend"].Value!;
        Assert.That(outputBlobId, Is.Not.EqualTo(sceneBlobId));

        var outputBlendPath = m_blobService.GetStoredPath(outputBlobId);
        var outputDirectory = Path.GetDirectoryName(outputBlendPath)!;
        var materializedLibraryPath = Path.Combine(outputDirectory, "deps", "linked-libraries", "library.blend");
        var materializedVolumePath = Path.Combine(outputDirectory, "deps", "volumes", "cloud.vdb");
        Assert.That(File.Exists(materializedLibraryPath), Is.True);
        Assert.That(File.Exists(materializedVolumePath), Is.True);
        Assert.That(File.ReadAllBytes(materializedLibraryPath), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(File.ReadAllBytes(materializedVolumePath), Is.EqualTo(new byte[] { 5, 6, 7, 8 }));
    }

    #endregion
}
