using System.Text.RegularExpressions;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests;

/// <summary>
/// What stands between a change and what a node is sent, held as text (audit findings V:M-8 and
/// V:L-9). A workflow cannot be run from a test, but what it must keep saying can be read: that
/// the committed runtime modules are compared with a rebuild before a merge and not only after
/// it, that the numpy archive and the other archives are held to the hashes the project file
/// pins, that an action is named by its commit, that nothing in the build files floats where
/// a version must be chosen, that a download without a pin stops a build before anything of it
/// is unpacked. Each test fails on the files as they were before hardening batch C.
/// </summary>
[TestFixture]
public sealed class VerifySupplyChainTests
{
    #region Constants

    private const string WORKFLOWS = ".github/workflows";
    private const string TOOLCHAIN_WORKFLOW = "ai-verify-toolchain.yml";
    private const string NUMPY_WORKFLOW = "ai-verify-python-numpy.yml";
    private const string ARCHIVES_WORKFLOW = "ai-verify-archives.yml";
    private const string TESTS_WORKFLOW = "ai-verify.yml";

    private const string TOOLCHAIN = "AI/OutWit.Controller.AI.Verify.Assets/toolchain";
    private const string CONTROLLER_PROPS = "Build/OutWit.Controller.props";
    private const string SCRIPTS_PROJECT = "AI/OutWit.Controller.AI.Verify.Scripts/OutWit.Controller.AI.Verify.Scripts.csproj";

    private const string ASSETS_PACKAGE = "OutWit.Engine.Assets.MSBuild";
    private const string CONTROLLER_PACKAGE = "OutWit.Controller.AI.Verify";

    private const string UNPINNED_STOP = "if [ \"$UNPINNED\" -ne 0 ]; then";
    private const string UNPINNED_EXIT = "exit 3";

    private static readonly string[] VERIFY_WORKFLOWS = [TOOLCHAIN_WORKFLOW, NUMPY_WORKFLOW, ARCHIVES_WORKFLOW, TESTS_WORKFLOW];

    // An action by its commit, with the version that commit is in a comment.
    private static readonly Regex USES = new(@"^\s*(?:-\s*)?uses:\s*(?<reference>\S+)(?<rest>.*)$", RegexOptions.Multiline);
    private static readonly Regex BY_COMMIT = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$");
    private static readonly Regex VERSION_COMMENT = new(@"^\s+#\s+v\d+\.\d+\.\d+\s*$");

    // What unpacks or runs a download in a toolchain script.
    private static readonly Regex UNPACKS = new(@"^\s*(?:\(\s*cd [^)]*&&\s*)?(?:tar\s+-x|unzip\s|python3\s+-I\s+-m\s+zipfile\s+-e)", RegexOptions.Multiline);

    #endregion

    #region Workflow Tests

    [Test]
    public void L9EveryActionIsNamedByItsCommitTest()
    {
        // A tag can be moved to another commit by whoever holds the action's repository; a
        // commit cannot. The comment keeps the version readable.
        var files = Directory.GetFiles(Path.Combine(Root(), WORKFLOWS.Replace('/', Path.DirectorySeparatorChar)), "*.yml");
        Assert.That(files, Is.Not.Empty);

        var uses = 0;
        Assert.Multiple(() =>
        {
            foreach (var file in files)
            {
                foreach (Match match in USES.Matches(Read(file)))
                {
                    uses++;
                    var where = $"{Path.GetFileName(file)}: uses {match.Groups["reference"].Value}";
                    Assert.That(BY_COMMIT.IsMatch(match.Groups["reference"].Value), Is.True, $"{where} is not an action at a commit");
                    Assert.That(VERSION_COMMENT.IsMatch(match.Groups["rest"].Value), Is.True, $"{where} does not say in a comment which version the commit is");
                }
            }
        });

        Assert.That(uses, Is.GreaterThan(20), "the workflows of the repository use actions");
    }

    [Test]
    public void L9TheVerifyWorkflowsMayReadTheRepositoryAndNothingElseTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var name in VERIFY_WORKFLOWS)
            {
                var block = Regex.Match(Workflow(name), @"^permissions:\n(?<lines>(?:[ \t]+\S.*\n)+)", RegexOptions.Multiline);

                Assert.That(block.Success, Is.True, $"{name} has no permissions block: its jobs would run with whatever the repository grants by default");
                Assert.That(block.Groups["lines"].Value.Trim(), Is.EqualTo("contents: read"), $"{name}: the least a job that builds and tests needs");
                Assert.That(Regex.IsMatch(Workflow(name), @"^[ \t]+permissions:", RegexOptions.Multiline), Is.False, $"{name}: no job widens it");
            }
        });
    }

    [Test]
    public void M8TheCommittedModulesAreComparedWithARebuildBeforeAMergeTest()
    {
        var workflow = Workflow(TOOLCHAIN_WORKFLOW);
        var push = Paths(workflow, "push");
        var pullRequest = Paths(workflow, "pull_request");

        Assert.Multiple(() =>
        {
            Assert.That(pullRequest, Is.Not.Empty, "the toolchain workflow does not run on pull requests: a change that replaces a module and moves its pins is merged unchecked");
            Assert.That(pullRequest, Is.EqualTo(push), "a pull request is held to what a merge is held to");
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify.Assets/modules/**"), "the committed modules");
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify.Assets/toolchain/**"), "what builds them, and its pins");
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify.Assets/AssetPins.cs"), "the asset tool's pins of the modules");
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify/Runtimes/VerifyRuntimeCatalog.cs"), "the controller's pins of the modules");

            // The step that fails a committed module that is not what the sources build to.
            Assert.That(workflow, Does.Contain("for item in lua-5.4.8/lua.wasm lua-5.4.8/LICENSE-lua.txt quickjs-0.15.1/qjs.wasm"));
            Assert.That(workflow, Does.Contain("is not what the toolchain builds"));

            // A build that stopped for a download without a pin is not a build to publish from.
            Assert.That(workflow, Does.Not.Contain("-eq 3"), "the workflow no longer takes 'a download has no pin' for a build");
        });
    }

    [Test]
    public void M8TheNumpyArchiveIsHeldToThePinOfTheProjectFileTest()
    {
        var workflow = Workflow(NUMPY_WORKFLOW);
        var pullRequest = Paths(workflow, "pull_request");

        Assert.Multiple(() =>
        {
            Assert.That(pullRequest, Is.EqualTo(Paths(workflow, "push")));
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify.Assets/toolchain/python-numpy/**"));
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify.Assets/AssetPins.cs"), "the archive's own pin (the project file's is held to it by a test)");

            // The compare job reads the pin from the project file and fails on another archive.
            Assert.That(workflow, Does.Contain("<ControllerDataAsset Include=\"verify-python-numpy\">"));
            Assert.That(workflow, Does.Contain("seen=$(sha256sum a/verify-python-numpy.zip | cut -d' ' -f1)"));
            Assert.That(workflow, Does.Contain("if [ -z \"$pin\" ] || [ \"$seen\" != \"$pin\" ] || [ \"$bytes\" != \"$size\" ]; then"));
            Assert.That(workflow, Does.Contain("is not the archive the project file pins"));
        });
    }

    [Test]
    public void M8TheArchivesAndTheNoticesAreHeldToTheirSourcesTest()
    {
        var workflow = Workflow(ARCHIVES_WORKFLOW);
        var pullRequest = Paths(workflow, "pull_request");
        var script = ReadRepository($"{TOOLCHAIN}/archives.sh");

        Assert.Multiple(() =>
        {
            Assert.That(pullRequest, Is.EqualTo(Paths(workflow, "push")));
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify/OutWit.Controller.AI.Verify.csproj"), "the pins");
            Assert.That(pullRequest, Does.Contain("AI/OutWit.Controller.AI.Verify.Assets/**"), "what the archives are built from");

            Assert.That(workflow, Does.Contain("toolchain/archives.sh \"$RUNNER_TEMP/archives\" \"$RUNNER_TEMP/work\" --check"), "the archives are rebuilt and held to the project file's pins");
            Assert.That(workflow, Does.Contain("toolchain/notices/notices.py check ."), "the committed notices are held to what the tool writes");

            // The check itself: an archive with another hash or size fails the run.
            Assert.That(script, Does.Contain("if [ \"$seen\" != \"$(pinned \"$asset\" Sha256)\" ] || [ \"$size\" != \"$(pinned \"$asset\" Size)\" ]; then"));
            Assert.That(script, Does.Contain("for asset in verify-wasm wasmtime-win-x64 wasmtime-linux-x64 wasmtime-osx-arm64; do"));
            Assert.That(script, Does.Contain("exit \"$status\""));
        });
    }

    #endregion

    #region Build File Tests

    [Test]
    public void M8NoPackageVersionFloatsInTheBuildFilesTest()
    {
        var root = Root();
        var files = Directory.GetFiles(Path.Combine(root, "Build"), "*.props")
            .Concat(Directory.GetFiles(Path.Combine(root, "Build"), "*.targets"))
            .Concat(Directory.GetFiles(Path.Combine(root, "AI"), "*.csproj", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "AI"), "*.targets", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.That(files, Has.Count.GreaterThan(10));

        Assert.Multiple(() =>
        {
            foreach (var file in files)
            {
                foreach (Match reference in Regex.Matches(WithoutComments(Read(file)), "<PackageReference\\s+Include=\"(?<id>[^\"]+)\"\\s+Version=\"(?<version>[^\"]+)\""))
                    Assert.That(reference.Groups["version"].Value, Does.Not.Contain("*"), $"{Path.GetRelativePath(root, file)}: {reference.Groups["id"].Value} floats");
            }

            var assets = Regex.Match(ReadRepository(CONTROLLER_PROPS), $"<PackageReference\\s+Include=\"{Regex.Escape(ASSETS_PACKAGE)}\"\\s+Version=\"(?<version>[^\"]+)\"");
            Assert.That(assets.Success, Is.True, $"{CONTROLLER_PROPS} references {ASSETS_PACKAGE}");
            Assert.That(assets.Groups["version"].Value, Does.Match(@"^\d+\.\d+\.\d+$"), $"{ASSETS_PACKAGE}, the package that holds a controller's assets to their hashes, is taken at one chosen version");
        });
    }

    [Test]
    public void L9TheScriptsPackageAcceptsTheReleaseItWaitsForTest()
    {
        // The marker is enabled when the controller is first published to nuget.org, as 1.0.0.
        // Its range must hold that version; the one it had, [0.1.0, 1.0.0), refused it.
        var marker = Regex.Match(ReadRepository(SCRIPTS_PROJECT),
            $"<PackageReference\\s+Include=\"{Regex.Escape(CONTROLLER_PACKAGE)}\"\\s+Version=\"\\[(?<lower>[0-9.]+),\\s*(?<upper>[0-9.]+)\\)\"");

        Assert.That(marker.Success, Is.True, "the scripts project carries its compatibility marker as a half-open range");

        var lower = Version.Parse(marker.Groups["lower"].Value);
        var upper = Version.Parse(marker.Groups["upper"].Value);
        var first = new Version(1, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(lower, Is.LessThanOrEqualTo(first), "the range starts at or before the controller's first public release");
            Assert.That(upper, Is.GreaterThan(first), "and does not end before it");
            Assert.That(upper, Is.EqualTo(new Version(lower.Major + 1, 0, 0)), "it ends at the next major, exclusive, as the decision record says");
        });
    }

    #endregion

    #region Toolchain Tests

    [TestCase(TOOLCHAIN + "/build.sh")]
    [TestCase(TOOLCHAIN + "/python-numpy/build.sh")]
    public void L9ADownloadWithoutAPinStopsTheBuildBeforeAnythingIsUnpackedTest(string script)
    {
        var text = ReadRepository(script);
        var stop = text.IndexOf(UNPINNED_STOP, StringComparison.Ordinal);
        var unpacks = UNPACKS.Matches(text);

        Assert.Multiple(() =>
        {
            Assert.That(stop, Is.GreaterThan(0), "the script stops for a download without a pin");
            Assert.That(text.IndexOf(UNPINNED_EXIT, stop, StringComparison.Ordinal), Is.InRange(stop, stop + 300), "with its own exit code");
            Assert.That(text.LastIndexOf(UNPINNED_EXIT, StringComparison.Ordinal), Is.LessThan(stop + 300), "and nowhere later: the stop is not at the end of the build");
            Assert.That(text.LastIndexOf("fetch \"$", StringComparison.Ordinal), Is.LessThan(stop), "every download is fetched and hashed before the stop");
            Assert.That(unpacks, Is.Not.Empty, "the script unpacks what it downloads");
            Assert.That(unpacks.Select(match => match.Index).Min(), Is.GreaterThan(stop), "nothing is unpacked before every download has matched its pin");
        });
    }

    #endregion

    #region Tools

    /// <summary>The paths a workflow's trigger is limited to, in the order written; empty when the trigger is not there.</summary>
    private static List<string> Paths(string workflow, string trigger)
    {
        var block = Regex.Match(workflow, $@"^  {Regex.Escape(trigger)}:\n    paths:\n(?<lines>(?:      .*\n)+)", RegexOptions.Multiline);
        if (!block.Success)
            return [];

        return block.Groups["lines"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim().Trim('\''))
            .ToList();
    }

    private static string WithoutComments(string xml)
    {
        return Regex.Replace(xml, "<!--.*?-->", "", RegexOptions.Singleline);
    }

    private static string Workflow(string name)
    {
        return ReadRepository($"{WORKFLOWS}/{name}");
    }

    private static string ReadRepository(string relativePath)
    {
        var path = Path.Combine(Root(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(path), Is.True, $"missing: {path}");
        return Read(path);
    }

    private static string Read(string path)
    {
        // A checkout may have either line end.
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    private static string Root()
    {
        var root = VerifyRigModules.SolutionRoot();
        if (root == null)
            VerifyRigModules.Missing("Solution root not found");

        return root;
    }

    #endregion
}
