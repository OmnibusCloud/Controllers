using OutWit.Controller.Visualization.ParaView.Runtime;
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
}
