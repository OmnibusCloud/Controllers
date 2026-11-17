using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The background cleanup of what a run leaves: a file or a folder handed over is deleted off
/// the caller's path, a path that is not there is fine, and a caller that needs the files gone
/// can wait for that.
/// </summary>
[TestFixture]
public sealed class WasmHostDiscardTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_discard_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
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
    public void AFileAndAFolderHandedOverAreDeletedTest()
    {
        var file = Path.Combine(m_root, "run.in");
        var folder = Path.Combine(m_root, "task.src");
        File.WriteAllText(file, "input");
        Directory.CreateDirectory(Path.Combine(folder, "lib"));
        File.WriteAllText(Path.Combine(folder, "lib", "util.lua"), "return 1");

        WasmHostDiscard.Path(file);
        WasmHostDiscard.Path(folder);

        Assert.Multiple(() =>
        {
            Assert.That(WasmHostDiscard.Drain(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(File.Exists(file), Is.False);
            Assert.That(Directory.Exists(folder), Is.False);
        });
    }

    [Test]
    public void APathThatIsNotThereIsFineTest()
    {
        WasmHostDiscard.Path(Path.Combine(m_root, "never-written.out"));

        Assert.That(WasmHostDiscard.Drain(TimeSpan.FromSeconds(30)), Is.True);
    }

    [Test]
    public void TheCleanupStartsAgainAfterItHasBeenIdleTest()
    {
        // The thread leaves when it has nothing to do; the next path starts another.
        for (var round = 0; round < 2; round++)
        {
            var files = Enumerable.Range(0, 200).Select(index => Path.Combine(m_root, $"{round}-{index}.out")).ToList();
            foreach (var file in files)
                File.WriteAllText(file, "");

            foreach (var file in files)
                WasmHostDiscard.Path(file);

            Assert.That(WasmHostDiscard.Drain(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(Directory.GetFiles(m_root), Is.Empty);

            if (round == 0)
                Thread.Sleep(2_500);
        }
    }

    #endregion
}
