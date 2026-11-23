using System.Text.Json;
using System.Xml.Linq;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests;

/// <summary>
/// What a consumer of the package gets, read from the files that decide it. The natives are not in
/// the package: they are data assets a consumer's build fetches, and that happens only when the
/// package's own targets ask for it. These tests hold the links of that chain: the project
/// declares the assets, the manifest the build stages carries every one of them with the project's
/// pins, the staged module carries what the controller needs beside itself and names its
/// dependency, and the capabilities schema is packed beside the README. The module-load proof
/// (<see cref="SmtModuleLoadTests"/>) then runs on an asset resolved that way.
/// </summary>
[TestFixture]
public sealed class SmtPackageLayoutTests
{
    #region Constants

    private const string PROJECT = "OutWit.Controller.AI.Smt.csproj";
    private const string MANIFEST = "controller.json";

    private static readonly string[] NODE_PLATFORMS = ["win-x64", "linux-x64", "osx-arm64"];

    #endregion

    #region Tests

    [Test]
    public void TheProjectDeclaresTheEngineLibraryOfEveryNodePlatformTest()
    {
        var assets = SmtPackagedAssets.Project(ProjectPath());

        Assert.Multiple(() =>
        {
            foreach (var platform in NODE_PLATFORMS)
            {
                var native = assets.Where(asset => asset.RuntimeIdentifier == platform).ToList();
                Assert.That(native, Has.Count.EqualTo(1), $"one native engine library for {platform}");
                Assert.That(native.FirstOrDefault()?.ExtractTo, Is.EqualTo($"runtimes/{platform}/native/"), "where the module's dependency file looks for it");
            }

            Assert.That(assets.Select(asset => asset.Sha256), Has.All.Length.EqualTo(64), "every asset is pinned by its SHA-256");
            Assert.That(assets.Select(asset => asset.Size), Has.All.GreaterThan(0));
            Assert.That(assets.Select(asset => asset.Uri), Has.All.StartWith("gh-release://OmnibusCloud/Controllers/"));
        });
    }

    [Test]
    public void TheStagedManifestCarriesEveryAssetTheProjectDeclaresAndTheDependencyTest()
    {
        var module = StagedModule();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(module, MANIFEST)));

        Assert.Multiple(() =>
        {
            Assert.That(SmtPackagedAssets.Declared(module), Is.EquivalentTo(SmtPackagedAssets.Project(ProjectPath())), "the manifest a package is made from names every asset, with the project's own pins");
            Assert.That(manifest.RootElement.GetProperty("dependencies").GetProperty("Variables").GetString(), Is.EqualTo("1.0.0"), "and the controller it depends on");
            Assert.That(manifest.RootElement.GetProperty("version").GetString(), Is.EqualTo(SmtNodeRig.ControllerVersion));
        });
    }

    [Test]
    public void TheStagedModuleCarriesWhatTheControllerNeedsBesideItselfTest()
    {
        var module = StagedModule();
        var files = Directory.GetFiles(module).Select(Path.GetFileName).ToList();

        Assert.That(files, Is.SupersetOf(new[]
        {
            "OutWit.Controller.AI.Smt.dll", "OutWit.Controller.AI.Smt.deps.json", "OutWit.Controller.AI.Smt.Model.dll", "Wasmtime.Dotnet.dll", "THIRD-PARTY-NOTICES.txt", MANIFEST
        }));
    }

    [Test]
    public void TheCapabilitiesSchemaIsPackedBesideTheReadmeTest()
    {
        var project = XDocument.Load(ProjectPath());
        var packed = project.Descendants("None")
            .Where(item => (string?)item.Attribute("Pack") == "true")
            .Select(item => (((string?)item.Attribute("Update") ?? (string?)item.Attribute("Include") ?? "").Replace('\\', '/'), ((string?)item.Attribute("PackagePath") ?? "").Replace('\\', '/')))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(packed, Does.Contain(("docs/smt-capabilities.schema.json", "docs/smt-capabilities.schema.json")));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(ProjectPath())!, "docs", "smt-capabilities.schema.json")), Is.True);
        });
    }

    #endregion

    #region Tools

    private static string ProjectPath()
    {
        var root = SmtRigModules.SolutionRoot();
        if (root == null)
            SmtRigModules.Missing("the solution root was not found");

        return Path.Combine(root!, "AI", "OutWit.Controller.AI.Smt", PROJECT);
    }

    private static string StagedModule()
    {
        var staged = SmtRigModules.StagedRoot();
        var module = staged == null ? null : Path.Combine(staged, SmtRigModules.SMT);
        if (module == null || !File.Exists(Path.Combine(module, MANIFEST)))
            SmtRigModules.Missing($"no staged smt.module at '{module}'");

        return module!;
    }

    #endregion
}
