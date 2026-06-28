using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Tests.Mock;
using OutWit.Controller.Render.Utils;

namespace OutWit.Controller.Render.Tests.Utils;

/// <summary>
/// Node-side attachment delivery: the host writes a "&lt;blend&gt;.attachments.json" sidecar and uploads
/// each dependency as a blob; the render node must download those blobs and lay them out next to a working
/// copy of the blend so its relative dependency references resolve. These cover the materialization,
/// sidecar reading, defensive blob resolution, and path-traversal guard without needing Blender.
/// </summary>
[TestFixture]
public sealed class RenderSceneAttachmentTransferTests
{
    #region Fields

    private string m_root = null!;
    private string m_storageDir = null!;
    private RenderTestBlobService m_blobService = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witcloud_attach_xfer_{Guid.NewGuid():N}");
        m_storageDir = Path.Combine(m_root, "storage");
        Directory.CreateDirectory(m_storageDir);
        m_blobService = new RenderTestBlobService(m_storageDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task MaterializeCopiesBlobToRelativePathTest()
    {
        var sourcePath = Path.Combine(m_root, "library_source.blend");
        await File.WriteAllTextAsync(sourcePath, "library-bytes");
        var blobId = m_blobService.RegisterExistingFile(sourcePath);

        var sceneDir = Path.Combine(m_root, "scene");
        Directory.CreateDirectory(sceneDir);

        await RenderSceneAttachmentTransfer.MaterializeAsync(m_blobService, sceneDir, [Attachment(blobId, "deps/lib/library.blend")]);

        var materialized = Path.Combine(sceneDir, "deps", "lib", "library.blend");
        Assert.That(File.Exists(materialized), Is.True);
        Assert.That(await File.ReadAllTextAsync(materialized), Is.EqualTo("library-bytes"));
    }

    [Test]
    public async Task MaterializeSkipsNonBlobPackagedEntriesTest()
    {
        var sceneDir = Path.Combine(m_root, "scene");
        Directory.CreateDirectory(sceneDir);

        var attachment = new RenderSceneAttachmentRefData
        {
            Kind = "LinkedLibrary",
            BlobId = Guid.Empty,
            RelativePath = "deps/lib/library.blend",
            PackagingStrategy = "InlinedInBlend"
        };

        // Non-SceneAttachmentBlob packaging is skipped — no throw, nothing copied.
        await RenderSceneAttachmentTransfer.MaterializeAsync(m_blobService, sceneDir, [attachment]);
        Assert.That(Directory.Exists(Path.Combine(sceneDir, "deps")), Is.False);
    }

    [Test]
    public async Task PrepareWorkingSceneCopiesBlendAndMaterializesAttachmentsTest()
    {
        var blendSource = Path.Combine(m_root, "scene_source.blend");
        await File.WriteAllTextAsync(blendSource, "blend-bytes");

        var libSource = Path.Combine(m_root, "lib_source.blend");
        await File.WriteAllTextAsync(libSource, "library-bytes");
        var libBlob = m_blobService.RegisterExistingFile(libSource);

        var (workingBlend, workingDir) = await RenderSceneAttachmentTransfer.PrepareWorkingSceneAsync(
            m_blobService, m_root, blendSource, [Attachment(libBlob, "deps/lib/library.blend")], Guid.NewGuid(), taskIndex: 0);

        Assert.That(File.Exists(workingBlend), Is.True, "working blend copy must exist");

        // The library must sit next to the working blend at the relative path the blend references.
        var sibling = Path.Combine(Path.GetDirectoryName(workingBlend)!, "deps", "lib", "library.blend");
        Assert.That(File.Exists(sibling), Is.True, "attachment must be materialized next to the working blend");

        RenderSceneAttachmentTransfer.TryDeleteWorkingScene(workingDir);
        Assert.That(Directory.Exists(workingDir), Is.False);
    }

    [Test]
    public async Task TryLoadManifestReadsSidecarAndIsEmptyWhenAbsentTest()
    {
        var blendPath = Path.Combine(m_root, "scene.blend");
        await File.WriteAllTextAsync(blendPath, "blend");

        Assert.That(RenderSceneAttachmentTransfer.TryLoadManifest(blendPath), Is.Empty);

        await File.WriteAllTextAsync(blendPath + ".attachments.json",
            JsonSerializer.Serialize(new[] { Attachment(Guid.NewGuid(), "deps/x.bin") }));

        var loaded = RenderSceneAttachmentTransfer.TryLoadManifest(blendPath);
        Assert.That(loaded, Has.Count.EqualTo(1));
        Assert.That(loaded[0].RelativePath, Is.EqualTo("deps/x.bin"));
    }

    [Test]
    public async Task TryLoadManifestForBlobReturnsEmptyForUnknownBlobTest()
    {
        // Defensive: an unresolved scene blob must not throw at split time.
        var result = await RenderSceneAttachmentTransfer.TryLoadManifestForBlobAsync(
            m_blobService, Guid.NewGuid(), NullLogger.Instance);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ResolveAttachmentTargetPathRejectsTraversalTest()
    {
        var sceneDir = Path.Combine(m_root, "scene");
        Directory.CreateDirectory(sceneDir);

        Assert.That(() => RenderSceneAttachmentTransfer.ResolveAttachmentTargetPath(sceneDir, "../escape.bin"),
            Throws.InvalidOperationException);
        Assert.That(() => RenderSceneAttachmentTransfer.ResolveAttachmentTargetPath(sceneDir, "deps/../../escape.bin"),
            Throws.InvalidOperationException);
    }

    #endregion

    #region Tools

    private static RenderSceneAttachmentRefData Attachment(Guid blobId, string relativePath)
    {
        return new RenderSceneAttachmentRefData
        {
            Kind = "LinkedLibrary",
            BlobId = blobId,
            OriginalPath = "//" + relativePath,
            RelativePath = relativePath,
            PackagingStrategy = "SceneAttachmentBlob"
        };
    }

    #endregion
}
