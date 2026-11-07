using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Runtime;

[TestFixture]
public class FoamInitialFieldsTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-initial-fields");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(m_case, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? m_case);
        File.WriteAllText(path, text);
    }

    private string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(m_case, relativePath));
    }

    private string LogPath => Path.Combine(m_case, "log.restore0Dir");

    #endregion

    #region Keep Tests

    [Test]
    public void KeepingCopiesTheWholeInitialFolderUnlessTheCaseHasItsOwnOrigTest()
    {
        Write("0/U", "U");
        Write("0/include/initialConditions", "flowVelocity (20 0 0);");

        FoamInitialFields.Keep(m_case);

        Assert.That(Read("0.orig/U"), Is.EqualTo("U"));
        Assert.That(Read("0.orig/include/initialConditions"), Is.EqualTo("flowVelocity (20 0 0);"), "the includes of the fields travel with them");

        Write("0/U", "changed by a step");
        FoamInitialFields.Keep(m_case);
        Assert.That(Read("0.orig/U"), Is.EqualTo("U"), "an existing 0.orig/ is the case's own and is never overwritten");
    }

    #endregion

    #region Restore Tests

    [Test]
    public void EveryProcessorGetsTheWholeInitialFolderInNumericOrderTest()
    {
        Write("0.orig/U", "U");
        Write("0.orig/include/initialConditions", "flowVelocity (20 0 0);");
        foreach (var processor in new[] { "processor10", "processor2", "processor0" })
            Write($"{processor}/0/U", "stale");
        Write("processor2/0/pointLevel", "left by the meshing");

        var outcome = FoamInitialFields.RestoreIntoProcessors(m_case, LogPath, decomposed: true);

        Assert.That(outcome.ExitCode, Is.EqualTo(0), outcome.LogTail);
        foreach (var processor in new[] { "processor0", "processor2", "processor10" })
        {
            Assert.That(Read($"{processor}/0/U"), Is.EqualTo("U"));
            Assert.That(Read($"{processor}/0/include/initialConditions"), Is.EqualTo("flowVelocity (20 0 0);"));
        }

        Assert.That(File.Exists(Path.Combine(m_case, "processor2", "0", "pointLevel")), Is.False);
        var log = File.ReadAllText(LogPath);
        Assert.That(log.IndexOf("processor2/0", StringComparison.Ordinal), Is.LessThan(log.IndexOf("processor10/0", StringComparison.Ordinal)), "processors in numeric order");
    }

    [Test]
    public void ASerialRunHasNothingToDoTest()
    {
        Write("0/U", "U");

        var outcome = FoamInitialFields.RestoreIntoProcessors(m_case, LogPath, decomposed: false);

        Assert.That(outcome.ExitCode, Is.EqualTo(0));
        Assert.That(File.ReadAllText(LogPath), Does.Contain("serial"));
    }

    [Test]
    public void ADecomposedRunWithoutProcessorDirectoriesFailsByNameTest()
    {
        Write("0.orig/U", "U");

        var missing = FoamInitialFields.RestoreIntoProcessors(m_case, LogPath, decomposed: true);
        var missingLog = File.ReadAllText(LogPath);

        Write("processors2/0/U", "collated");
        var collated = FoamInitialFields.RestoreIntoProcessors(m_case, LogPath, decomposed: true);

        Assert.That(missing.ExitCode, Is.Not.EqualTo(0));
        Assert.That(missingLog, Does.Contain("decomposePar made no processor directories"));
        Assert.That(collated.ExitCode, Is.Not.EqualTo(0));
        Assert.That(collated.LogTail, Does.Contain("processors2").And.Contain("collated"), "the collated layout is named: the case's controlDict chose it");
    }

    [Test]
    public void ACaseWithoutInitialFieldsFailsByNameTest()
    {
        Write("processor0/constant/polyMesh/owner", "mesh");

        var outcome = FoamInitialFields.RestoreIntoProcessors(m_case, LogPath, decomposed: true);

        Assert.That(outcome.ExitCode, Is.Not.EqualTo(0));
        Assert.That(outcome.LogTail, Does.Contain("no initial fields"));
    }

    #endregion
}
