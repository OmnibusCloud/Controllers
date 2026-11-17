using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// A rig loads the modules it names and no other, and never a module another build left
/// (found on the test fleet's proof of batch A: a staged <c>corpus.module</c> failed every
/// Verify fixture's set-up, and a stale module was what got tested). The checks run on copies
/// of this build's staged modules in a folder of the test's own.
/// </summary>
[TestFixture]
public sealed class VerifyRigModulesTests
{
    #region Constants

    private const string FOREIGN_MODULE = "corpus.module";

    #endregion

    #region Fields

    private string m_folder = null!;
    private string m_staged = null!;
    private string? m_solutionRoot;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var staged = VerifyRigModules.StagedRoot();
        m_solutionRoot = VerifyRigModules.SolutionRoot();
        if (staged == null || VerifyRigModules.SCRIPTS.Any(module => !Directory.Exists(Path.Combine(staged, module))))
        {
            var reason = $"the build staged no modules under '{staged}'";
            if (VerifyRigModules.OnTheBuildServer)
                Assert.Fail(reason);

            Assert.Ignore(reason);
        }

        // A staging folder of the test's own: this build's modules, to be spoilt at will.
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_verify_rig_{Guid.NewGuid():N}");
        m_staged = Path.Combine(m_folder, "staged");
        foreach (var module in VerifyRigModules.SCRIPTS)
            CopyTree(Path.Combine(staged!, module), Path.Combine(m_staged, module));
    }

    [TearDown]
    public void TearDown()
    {
        if (m_folder != null)
            VerifyRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Tests

    [Test]
    public void ARigLoadsOnlyTheModulesItNamesTest()
    {
        // Another controller's module beside them, as the Corpus build leaves one, and a folder
        // that is no module at all.
        CopyTree(Path.Combine(m_staged, VerifyRigModules.SPECIAL), Path.Combine(m_staged, FOREIGN_MODULE));
        Directory.CreateDirectory(Path.Combine(m_staged, "_aside"));

        var rig = VerifyRigModules.Stage(m_staged, Path.Combine(m_folder, "rig"), VerifyRigModules.SCRIPTS, m_solutionRoot, TestContext.CurrentContext.TestDirectory);

        Assert.That(Directory.GetDirectories(rig).Select(Path.GetFileName), Is.EquivalentTo(VerifyRigModules.SCRIPTS),
            "the foreign module is not in the folder the engine is given");
    }

    [Test]
    public void TheVerifyModuleOfARigHoldsItsOwnFilesOnlyTest()
    {
        // What an earlier proof left inside the staged module (natives, language runtimes) is not
        // the module: a rig that needs them puts in the ones it means to test.
        var leftovers = Path.Combine(m_staged, VerifyRigModules.VERIFY, "runtimes", "any-rid", "native");
        Directory.CreateDirectory(leftovers);
        File.WriteAllText(Path.Combine(leftovers, "left.bin"), "left by an earlier run");

        var rig = VerifyRigModules.Stage(m_staged, Path.Combine(m_folder, "rig"), VerifyRigModules.CONTROLLER, m_solutionRoot, TestContext.CurrentContext.TestDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetDirectories(Path.Combine(rig, VerifyRigModules.VERIFY)), Is.Empty);
            Assert.That(File.Exists(Path.Combine(rig, VerifyRigModules.VERIFY, "controller.json")), Is.True);
            Assert.That(Directory.GetDirectories(rig).Select(Path.GetFileName), Is.EquivalentTo(VerifyRigModules.CONTROLLER));
        });
    }

    [Test]
    public void TheModulesThisBuildStagedAreItsOwnTest()
    {
        foreach (var module in VerifyRigModules.SCRIPTS)
            Assert.That(VerifyRigModules.Problem(m_staged, module, m_solutionRoot, TestContext.CurrentContext.TestDirectory), Is.Null, module);
    }

    [TestCase(VerifyRigModules.VERIFY, "OutWit.Controller.AI.Verify.dll")]
    [TestCase(VerifyRigModules.GRID, "OutWit.Controller.Grid.dll")]
    [TestCase(VerifyRigModules.VARIABLES, "OutWit.Controller.Variables.dll")]
    public void AModuleAnotherBuildLeftIsRefusedByNameTest(string module, string assembly)
    {
        // The same module with other bytes in its controller: what an earlier build, or a build
        // of another branch, leaves in the shared staging folder.
        File.AppendAllText(Path.Combine(m_staged, module, assembly), "stale");

        var problem = VerifyRigModules.Problem(m_staged, module, m_solutionRoot, TestContext.CurrentContext.TestDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(problem, Does.StartWith($"the staged {module} is stale: its {assembly} is not "));
            foreach (var other in VerifyRigModules.SCRIPTS.Where(other => other != module))
                Assert.That(VerifyRigModules.Problem(m_staged, other, m_solutionRoot, TestContext.CurrentContext.TestDirectory), Is.Null, other);
        });
    }

    [Test]
    public void AModuleThatIsNotStagedIsNamedTest()
    {
        Directory.Delete(Path.Combine(m_staged, VerifyRigModules.GRID), recursive: true);

        Assert.That(VerifyRigModules.Problem(m_staged, VerifyRigModules.GRID, m_solutionRoot, TestContext.CurrentContext.TestDirectory),
            Does.StartWith($"{VerifyRigModules.GRID} is not staged under "));
    }

    #endregion

    #region Tools

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        // The Verify module's own files only: what earlier runs left inside it can be large.
        var option = Path.GetFileName(source) == VerifyRigModules.VERIFY ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
        foreach (var file in Directory.GetFiles(source, "*", option))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    #endregion
}
