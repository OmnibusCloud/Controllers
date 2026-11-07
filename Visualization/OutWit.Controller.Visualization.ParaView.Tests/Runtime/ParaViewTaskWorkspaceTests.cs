using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Controller.Visualization.ParaView.Runtime;
using OutWit.Controller.Visualization.ParaView.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Runtime;

/// <summary>
/// The task workspace's attempt hygiene (audit C-M2): a retry after a crashed runner must start
/// without the previous attempt's status document or outputs, while the materialized package stays.
/// </summary>
[TestFixture]
public sealed class ParaViewTaskWorkspaceTests
{
    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"pv_workspace_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    [Test]
    public void ClearAttemptArtifactsRemovesTheStatusAndEveryOutputButKeepsThePackageTest()
    {
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, "temp"));
        using var workspace = ParaViewTaskWorkspace.Create(tempStorage, Guid.NewGuid(), 3);

        Directory.CreateDirectory(workspace.OutputDirectory);
        Directory.CreateDirectory(workspace.PackageRoot);
        File.WriteAllText(workspace.StatusFilePath, "{\"ok\": false, \"stage\": \"render\", \"error\": \"attempt 1\"}");
        File.WriteAllText(Path.Combine(workspace.OutputDirectory, "frame_000000.png"), "partial");
        File.WriteAllText(Path.Combine(workspace.OutputDirectory, "frame_000000.png.tmp"), "partial");
        var packageFile = Path.Combine(workspace.PackageRoot, "scene.pvsm");
        File.WriteAllText(packageFile, "<ServerManagerState/>");

        workspace.ClearAttemptArtifacts();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(workspace.StatusFilePath), Is.False, "the crashed attempt's status is gone");
            Assert.That(Directory.EnumerateFiles(workspace.OutputDirectory), Is.Empty, "no output of the crashed attempt survives");
            Assert.That(File.Exists(packageFile), Is.True, "the materialized package is untouched");
        });

        // Idempotent on an already clean workspace, and tolerant of a missing output directory.
        Directory.Delete(workspace.OutputDirectory, recursive: true);
        Assert.DoesNotThrow(workspace.ClearAttemptArtifacts);
    }

    #region Materialization declarations (audit C-M5 - fail closed)

    [Test]
    public async Task UndeclaredDigestAndSizeAreStampedFromTheBlobTest()
    {
        // The compose contract: a data scene may leave sha/size empty and the node stamps them.
        var (workspace, blobs, blobId) = await ArrangeAsync("undeclared", "twelve bytes"u8.ToArray());

        var materialized = await workspace.MaterializeAttachmentAsync(
            blobs, new ParaViewAttachmentRefData { BlobId = blobId, LogicalPath = "data/result.frd" }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(materialized.Size, Is.EqualTo(12));
            Assert.That(materialized.Sha256, Has.Length.EqualTo(64));
            Assert.That(File.Exists(materialized.Path), Is.True);
        });
    }

    [Test]
    public async Task MalformedDeclaredDigestFailsClosedTest()
    {
        var (workspace, blobs, blobId) = await ArrangeAsync("malformed-sha", "bytes"u8.ToArray());

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => workspace.MaterializeAttachmentAsync(
            blobs, new ParaViewAttachmentRefData { BlobId = blobId, LogicalPath = "data/result.frd", Sha256 = "not-a-digest" }, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("malformed SHA-256"));
        Assert.That(File.Exists(Path.Combine(workspace.PackageRoot, "data", "result.frd")), Is.False, "nothing is materialized on a corrupt declaration");
    }

    [Test]
    public async Task NegativeDeclaredSizeFailsClosedTest()
    {
        var (workspace, blobs, blobId) = await ArrangeAsync("negative-size", "bytes"u8.ToArray());

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => workspace.MaterializeAttachmentAsync(
            blobs, new ParaViewAttachmentRefData { BlobId = blobId, LogicalPath = "data/result.frd", Size = -1 }, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("negative size"));
    }

    [Test]
    public async Task WellFormedWrongDigestIsStillAMismatchTest()
    {
        var (workspace, blobs, blobId) = await ArrangeAsync("wrong-sha", "bytes"u8.ToArray());

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => workspace.MaterializeAttachmentAsync(
            blobs, new ParaViewAttachmentRefData { BlobId = blobId, LogicalPath = "data/result.frd", Sha256 = new string('a', 64) }, CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("content digest mismatch"));
        Assert.That(File.Exists(Path.Combine(workspace.PackageRoot, "data", "result.frd")), Is.False, "a mismatching copy is removed");
    }

    private async Task<(ParaViewTaskWorkspace Workspace, ParaViewTestBlobService Blobs, Guid BlobId)> ArrangeAsync(string label, byte[] bytes)
    {
        var tempStorage = new WitTempStorageDefault(Path.Combine(m_root, label, "temp"));
        var workspace = ParaViewTaskWorkspace.Create(tempStorage, Guid.NewGuid(), 0);
        var blobs = new ParaViewTestBlobService(Path.Combine(m_root, label, "blobs"));
        var blobId = await blobs.UploadBytesAsync(bytes, "result.frd");
        return (workspace, blobs, blobId);
    }

    #endregion
}
