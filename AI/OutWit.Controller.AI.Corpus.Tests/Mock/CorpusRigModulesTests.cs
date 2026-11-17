namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A rig loads only the modules it names, and only this build's. The engine loads every module of
/// the folder it is given, and the folder the builds stage into is shared by every controller of
/// the repository: a staged module of another controller was loaded into the Corpus rigs with the
/// rest, and a module left by an older build was tested in place of the new one, neither with a
/// word. The rig now copies its named set into a folder of its own, and a module that is missing,
/// mixed from two builds or built from something else fails the rig by name.
/// </summary>
[TestFixture]
public sealed class CorpusRigModulesTests
{
    #region Fields

    private string m_root = null!;
    private string m_source = null!;
    private string m_staged = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_rig_{Guid.NewGuid():N}");
        m_source = Path.Combine(m_root, "@Controllers", "Release");
        m_staged = Path.Combine(m_root, "@Rig");
        Directory.CreateDirectory(m_source);
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_root);
    }

    #endregion

    #region Tests

    [Test]
    public void AForeignModuleInTheSharedFolderIsNotLoadedTest()
    {
        // The shared folder holds what the rig names and what other builds left there.
        Module("alpha.module", ("Alpha.dll", "alpha"));
        Module("beta.module", ("Beta.dll", "beta"), ("Resources/data.bin", "kept out of the digest"));
        Module("verify.module", ("Verify.dll", "another controller's"));
        Module("render.module", ("Render.dll", "another controller's"));

        var staged = CorpusRigModules.Stage(m_source, m_staged, ["alpha.module", "beta.module"], new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetDirectories(staged).Select(Path.GetFileName), Is.EquivalentTo(new[] { "alpha.module", "beta.module" }), "the named set and nothing else");
            Assert.That(File.ReadAllText(Path.Combine(staged, "alpha.module", "Alpha.dll")), Is.EqualTo("alpha"));
            Assert.That(File.Exists(Path.Combine(staged, "beta.module", "Resources", "data.bin")), Is.True, "a module is copied whole");
        });
    }

    [Test]
    public void WhatAnEarlierRunStagedIsGoneTest()
    {
        Module("alpha.module", ("Alpha.dll", "alpha"));
        Directory.CreateDirectory(Path.Combine(m_staged, "stale.module"));
        File.WriteAllText(Path.Combine(m_staged, "stale.module", "Stale.dll"), "left by an earlier run");

        var staged = CorpusRigModules.Stage(m_source, m_staged, ["alpha.module"], new Dictionary<string, string>());

        Assert.That(Directory.GetDirectories(staged).Select(Path.GetFileName), Is.EqualTo(new[] { "alpha.module" }));
    }

    [Test]
    public void AMissingModuleFailsTheRigByNameTest()
    {
        Module("alpha.module", ("Alpha.dll", "alpha"));

        var failure = Assert.Throws<InvalidOperationException>(() => CorpusRigModules.Stage(m_source, m_staged, ["alpha.module", "grid.module"], new Dictionary<string, string>()));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.StartWith("The rig cannot load grid.module: the module is not staged."));
            Assert.That(Directory.Exists(m_staged), Is.False, "nothing is staged when one module cannot be");
        });
    }

    [Test]
    public void AModuleMixedFromTwoBuildsFailsTheRigTest()
    {
        // The manifest was stamped over one set of files; a file of the folder is another build's.
        Module("alpha.module", ("Alpha.dll", "alpha"), ("Alpha.Model.dll", "model"));
        File.WriteAllText(Path.Combine(m_source, "alpha.module", "Alpha.Model.dll"), "the model of an older build");

        var changed = Assert.Throws<InvalidOperationException>(() => CorpusRigModules.Stage(m_source, m_staged, ["alpha.module"], new Dictionary<string, string>()));

        // A file the build no longer makes, left in the folder.
        Module("beta.module", ("Beta.dll", "beta"));
        File.WriteAllText(Path.Combine(m_source, "beta.module", "Leftover.dll"), "left by an older build");

        var leftover = Assert.Throws<InvalidOperationException>(() => CorpusRigModules.Stage(m_source, m_staged, ["beta.module"], new Dictionary<string, string>()));

        Assert.Multiple(() =>
        {
            Assert.That(changed!.Message, Does.StartWith("The rig cannot load alpha.module: its files are not the ones its manifest was stamped over"));
            Assert.That(leftover!.Message, Does.StartWith("The rig cannot load beta.module: its files are not the ones its manifest was stamped over"));
        });
    }

    [Test]
    public void AModuleOfAnotherBuildFailsTheRigTest()
    {
        // The module is whole, and it is not the build under test: its assembly is another one.
        Module("alpha.module", ("Alpha.dll", "an older build of alpha"));
        var built = Path.Combine(m_root, "bin", "Alpha.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(built)!);
        File.WriteAllText(built, "this build of alpha");

        var failure = Assert.Throws<InvalidOperationException>(() => CorpusRigModules.Stage(m_source, m_staged, ["alpha.module"], new Dictionary<string, string> { ["alpha.module"] = built }));

        File.WriteAllText(built, "an older build of alpha");
        var staged = CorpusRigModules.Stage(m_source, m_staged, ["alpha.module"], new Dictionary<string, string> { ["alpha.module"] = built });

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.StartWith("The rig cannot load alpha.module: its Alpha.dll is not the one this build made"));
            Assert.That(failure.Message, Does.EndWith("Rebuild the test project (it builds the modules it loads)."));
            Assert.That(Directory.Exists(Path.Combine(staged, "alpha.module")), Is.True, "the same bytes: the module is this build's");
        });
    }

    [Test]
    public void TheRigsOfThisRunLoadTheNamedSetOfThisBuildTest()
    {
        // The real thing: the folder every Corpus rig of this run gives the engine.
        var folder = CorpusRigModules.Folder;
        var corpus = Path.Combine(folder, "corpus.module", "OutWit.Controller.AI.Corpus.dll");

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetDirectories(folder).Select(Path.GetFileName), Is.EquivalentTo(CorpusRigModules.NAMES));
            Assert.That(File.ReadAllBytes(corpus), Is.EqualTo(File.ReadAllBytes(typeof(WitControllerCorpusModule).Assembly.Location)), "the controller the rigs run is the assembly the tests run against");
            foreach (var name in CorpusRigModules.NAMES)
                Assert.That(CorpusRigModules.Problem(Path.Combine(folder, name), null), Is.Null, name);
        });
    }

    #endregion

    #region Tools

    /// <summary>A module folder as a build stages it: its files and a manifest stamped with their digest.</summary>
    private void Module(string name, params (string File, string Content)[] files)
    {
        var folder = Path.Combine(m_source, name);
        foreach (var (file, content) in files)
        {
            var path = Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        File.WriteAllText(Path.Combine(folder, "controller.json"), $$"""{ "name": "{{name}}", "contentDigest": "{{CorpusRigModules.Digest(folder)}}" }""");
    }

    #endregion
}
