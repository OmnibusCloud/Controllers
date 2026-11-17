using System.Text.Json;
using System.Xml.Linq;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests;

/// <summary>
/// What a consumer of the package gets, read from the files that decide it (audit finding
/// V:M-7: no test looked at the package from the consumer's side). The natives and the language
/// runtimes are not in the package: they are data assets a consumer's build fetches, and that
/// happens only when the package's own targets ask for it. Corpus 0.2.0 declared its assets
/// and shipped without the targets that resolve them; nothing failed until a fleet ran it.
/// These tests hold the three links of that chain: the project declares the assets, the
/// manifest the build stages carries every one of them, and the targets the package ships
/// resolve the manifest. The module-load proof then runs on an asset resolved that way.
/// </summary>
[TestFixture]
public sealed class VerifyPackageLayoutTests
{
    #region Constants

    private const string PROJECT = "OutWit.Controller.AI.Verify.csproj";
    private const string TARGETS = "build/OutWit.Controller.AI.Verify.targets";
    private const string ASSET_ITEM = "ControllerDataAsset";
    private const string RESOLVE_TASK = "ResolveControllerAssetsTask";
    private const string MANIFEST = "controller.json";

    private static readonly string[] NODE_PLATFORMS = ["win-x64", "linux-x64", "osx-arm64"];

    #endregion

    #region Tests

    [Test]
    public void M7TheProjectDeclaresTheEngineLibraryOfEveryNodePlatformTest()
    {
        var assets = ProjectAssets();

        Assert.Multiple(() =>
        {
            foreach (var platform in NODE_PLATFORMS)
            {
                var native = assets.Where(asset => asset.RuntimeIdentifier == platform).ToList();
                Assert.That(native, Has.Count.EqualTo(1), $"one native engine library for {platform}");
                Assert.That(native.FirstOrDefault()?.ExtractTo, Is.EqualTo($"runtimes/{platform}/native/"), "where the module's dependency file looks for it");
            }

            Assert.That(assets.Where(asset => asset.ExtractTo.StartsWith("wasm/", StringComparison.Ordinal)), Is.Not.Empty, "the language runtimes");
            Assert.That(assets.Select(asset => asset.Sha256), Has.All.Length.EqualTo(64), "every asset is pinned by its SHA-256");
            Assert.That(assets.Select(asset => asset.Size), Has.All.GreaterThan(0));
            Assert.That(assets.Select(asset => asset.Uri), Has.All.StartWith("gh-release://OmnibusCloud/Controllers/"));
        });
    }

    [Test]
    public void M7ThePackageShipsTheTargetsThatResolveItsAssetsTest()
    {
        // Assets that are declared and never resolved are a package that ships without them.
        var project = XDocument.Parse(Load(PROJECT));
        var packed = project.Descendants("None").Where(item => (string?)item.Attribute("Pack") == "true").ToList();
        var targets = XDocument.Parse(Load(TARGETS));
        var resolving = targets.Descendants(RESOLVE_TASK).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ProjectAssets(), Is.Not.Empty);
            Assert.That(packed.Select(item => ((string?)item.Attribute("Include"))?.Replace('\\', '/')), Does.Contain(TARGETS), "the targets file is packed");
            Assert.That(packed.Where(item => ((string?)item.Attribute("Include"))?.Replace('\\', '/') == TARGETS).Select(item => ((string?)item.Attribute("PackagePath"))?.TrimEnd('\\', '/')),
                Is.EqualTo(new[] { "build" }), "into build/, where a consumer's project imports it without being asked");
            Assert.That(resolving, Has.Count.EqualTo(1), $"the targets call {RESOLVE_TASK}");
            Assert.That((string?)resolving.FirstOrDefault()?.Attribute("ManifestPath"), Does.EndWith(MANIFEST), "on the module's own manifest");
            Assert.That((string?)resolving.FirstOrDefault()?.Attribute("ResolveForAllPlatforms"), Is.EqualTo("true"), "a server packs for every node platform");
            Assert.That((string?)resolving.FirstOrDefault()?.Parent?.Attribute("AfterTargets"), Is.EqualTo((string?)targets.Descendants("Target").First().Attribute("Name")),
                "after the module is copied into the consumer's output");
        });
    }

    [Test]
    public void M7TheStagedManifestCarriesEveryAssetTheProjectDeclaresTest()
    {
        var staged = VerifyRigModules.StagedRoot();
        var manifest = staged == null ? null : Path.Combine(staged, VerifyRigModules.VERIFY, MANIFEST);
        if (manifest == null || !File.Exists(manifest))
            VerifyRigModules.Missing($"no staged manifest at '{manifest}'");

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var declared = document.RootElement.GetProperty("dataAssets").EnumerateArray()
            .Select(item => new VerifyPackagedAsset(
                item.GetProperty("id").GetString() ?? "",
                item.GetProperty("runtimeIdentifier").GetString() ?? "",
                item.GetProperty("uri").GetString() ?? "",
                item.GetProperty("sha256").GetString() ?? "",
                item.GetProperty("size").GetInt64(),
                item.GetProperty("extractTo").GetString() ?? ""))
            .ToList();

        Assert.That(declared, Is.EquivalentTo(ProjectAssets()), "the manifest a package is made from names every asset, with the project's own pins");
    }

    #endregion

    #region Tools

    private static List<VerifyPackagedAsset> ProjectAssets()
    {
        return XDocument.Parse(Load(PROJECT)).Descendants(ASSET_ITEM)
            .Select(item => new VerifyPackagedAsset(
                (string?)item.Attribute("Include") ?? "",
                (string?)item.Element("RuntimeIdentifier") ?? "any",
                (string?)item.Element("Uri") ?? "",
                (string?)item.Element("Sha256") ?? "",
                long.Parse((string?)item.Element("Size") ?? "0"),
                (string?)item.Element("ExtractTo") ?? ""))
            .ToList();
    }

    private static string Load(string relativePath)
    {
        var root = VerifyRigModules.SolutionRoot();
        if (root == null)
            VerifyRigModules.Missing("Solution root not found");

        var path = Path.Combine(root, "AI", "OutWit.Controller.AI.Verify", relativePath);
        Assert.That(File.Exists(path), Is.True, $"missing: {path}");
        return File.ReadAllText(path);
    }

    #endregion
}
