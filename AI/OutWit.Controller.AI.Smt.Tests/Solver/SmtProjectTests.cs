using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The controller's project, held as text and as what it builds to. The cloud client loads every
/// controller into one shared load context, where the wasmtime binding and its native library
/// load once: Smt references the binding Verify and Corpus reference, and ships the same three
/// native libraries, from the same release, by the same hashes and sizes, as the host's natives
/// guard names them. The module carries the binding beside the controller, with its notices;
/// the consumer targets stage the module under its own folder and resolve its data assets.
/// Nothing is downloaded.
/// </summary>
[TestFixture]
public sealed class SmtProjectTests
{
    #region Constants

    private const string SOLUTION_FILE = "OutWit.slnx";
    private const string SMT = "OutWit.Controller.AI.Smt";
    private const string VERIFY = "OutWit.Controller.AI.Verify";
    private const string CORPUS = "OutWit.Controller.AI.Corpus";

    private const string BINDING_PACKAGE = "Wasmtime";
    private const string BINDING_ASSEMBLY = "Wasmtime.Dotnet";
    private const string NOTICES = "THIRD-PARTY-NOTICES.txt";
    private const string BINDING_LICENSE = "OutWit.Controller.AI.Corpus/Notices/texts/Wasmtime.Dotnet-48.0.2-LICENSE.txt";

    #endregion

    #region Native Tests

    [Test]
    public void TheThreeControllersShipTheSameNativeLibrariesTest()
    {
        var smt = Natives(SMT);
        var verify = Natives(VERIFY);
        var corpus = Natives(CORPUS);

        Assert.Multiple(() =>
        {
            Assert.That(smt, Is.Not.Empty);
            Assert.That(smt, Is.EqualTo(verify), "the same platforms, addresses, hashes, sizes and folders as Verify's");
            Assert.That(smt, Is.EqualTo(corpus), "and as Corpus's");
            Assert.That(smt.Select(native => native.Split('|')[0]), Is.EquivalentTo(WasmHostNatives.PINNED.Keys), "one library for each platform the host's natives guard pins");

            foreach (var native in smt)
            {
                var cells = native.Split('|');
                Assert.That(cells[1], Is.EqualTo($"gh-release://OmnibusCloud/Controllers/{WasmHostNatives.RELEASE}/wasmtime-{cells[0]}.zip"), "from the release the guard names");
                Assert.That(cells[4], Is.EqualTo($"runtimes/{cells[0]}/native/"), "where the module's deps.json looks for it");
            }
        });
    }

    [Test]
    public void TheThreeControllersReferenceTheBindingTheHostIsBuiltAgainstTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var project in new[] { SMT, VERIFY, CORPUS, $"{SMT}.Tests" })
                Assert.That(BindingVersion(project), Is.EqualTo(WasmHostBinding.BUILT_AGAINST), project);

            Assert.That(typeof(Wasmtime.Engine).Assembly.GetName().Version?.ToString(3), Is.EqualTo(WasmHostBinding.BUILT_AGAINST), "and the binding the tests run on is that one");
        });
    }

    [Test]
    public void TheSolverModuleIsNoDataAssetYetTest()
    {
        // No release holds the module: an asset that names none would fail at every consumer.
        var assets = Project(SMT).Descendants("ControllerDataAsset").Select(asset => (string?)asset.Attribute("Include")).ToList();

        Assert.That(assets, Is.EqualTo(new[] { "wasmtime-win-x64", "wasmtime-linux-x64", "wasmtime-osx-arm64" }));
    }

    #endregion

    #region Module Tests

    [Test]
    public void TheModuleIsNamedSmtAndRegistersNothingYetTest()
    {
        var manifest = typeof(WitControllerSmtModule).GetCustomAttribute<WitPluginManifestAttribute>();
        var services = new ServiceCollection();

        new WitControllerSmtModule().Initialize(services);

        Assert.Multiple(() =>
        {
            Assert.That(manifest?.Name, Is.EqualTo("Smt"));
            Assert.That(manifest?.Version, Is.EqualTo((string?)Project(SMT).Descendants("Version").Single()));
            Assert.That(services, Is.Empty, "this version holds the solver: no variable, no activity");
        });
    }

    [Test]
    public void TheModuleCarriesTheBindingBesideTheControllerTest()
    {
        var project = Project(SMT);

        Assert.Multiple(() =>
        {
            Assert.That(project.Descendants("ControllerModuleAssembly").Select(assembly => (string?)assembly.Attribute("Include")), Is.EqualTo(new[] { BINDING_ASSEMBLY }));
            Assert.That(project.Descendants("ControllerModuleFile").Select(file => (string?)file.Attribute("Include")), Has.Some.EndsWith(NOTICES), "with the notices of what is not the controller's own");
            Assert.That(project.Descendants("Compile").Select(item => ((string?)item.Attribute("Include") ?? "").Replace('/', '\\')), Has.Some.Contains("Shared\\Wasm"), "the host is compiled in: the controller's own copy");
        });
    }

    [Test]
    public void TheConsumerTargetsStageTheModuleUnderItsOwnFolderAndResolveItsAssetsTest()
    {
        var targets = File.ReadAllText(Path.Combine(AiRoot(), SMT, "build", $"{SMT}.targets"));
        var folders = Regex.Matches(targets, @"@Controllers\\([a-z]+)\.module").Select(match => match.Groups[1].Value).Distinct().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(folders, Is.EqualTo(new[] { "smt" }), "the consumer targets stage into smt.module and nothing else");
            Assert.That(XDocument.Parse(targets).Descendants().Any(element => element.Name.LocalName == "ResolveControllerAssetsTask"), Is.True,
                "the native libraries reach a consumer only through the resolve step");
            Assert.That(Project(SMT).Descendants("None").Select(item => (string?)item.Attribute("Include")), Does.Contain($"build\\{SMT}.targets"), "and the targets are packed");
        });
    }

    #endregion

    #region Notices Tests

    [Test]
    public void TheNoticesNameTheBindingTheBuildRestoredAndGiveItsLicenceTextTest()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AiRoot(), SMT, NOTICES));
        var notices = Encoding.UTF8.GetString(bytes);
        var binding = Path.Combine(TestContext.CurrentContext.TestDirectory, $"{BINDING_ASSEMBLY}.dll");
        var license = File.ReadAllText(Path.Combine(AiRoot(), BINDING_LICENSE.Replace('/', Path.DirectorySeparatorChar))).ReplaceLineEndings("\n");

        Assert.Multiple(() =>
        {
            Assert.That(notices, Does.Not.Contain("\r"), "the file is the same bytes on every checkout: the module's content digest covers it");
            Assert.That(notices, Does.StartWith("THIRD-PARTY NOTICES\nThe module of the OmnibusCloud Smt controller\n"));
            Assert.That(notices, Does.Contain($"  1. {BINDING_ASSEMBLY} {WasmHostBinding.BUILT_AGAINST}: Apache-2.0 WITH LLVM-exception\n"), "the version the controller is built against");
            Assert.That(notices, Does.Contain($"File:      {BINDING_ASSEMBLY}.dll, as the package ships it (SHA-256 {Sha256(File.ReadAllBytes(binding))})\n"), "the assembly beside the tests is the one the notices are about");
            Assert.That(notices, Does.Contain($"(SHA-256 of that file: {Sha256(Encoding.UTF8.GetBytes(license))})\n"), "the licence text is held to its hash");
            Assert.That(notices, Does.EndWith(license + (char)10), "and is given whole, with a line after it");
            Assert.That(Regex.Matches(notices, @"^\d+\. ", RegexOptions.Multiline), Has.Count.EqualTo(1), "one part: the module carries nothing else of others");
        });
    }

    #endregion

    #region Tools

    /// <summary>The project's <c>wasmtime-*</c> data assets, each as <c>rid|uri|sha256|size|extractTo</c>, in project order.</summary>
    private static List<string> Natives(string project)
    {
        return Project(project).Descendants("ControllerDataAsset")
            .Where(asset => ((string?)asset.Attribute("Include") ?? "").StartsWith("wasmtime-", StringComparison.Ordinal))
            .Select(asset => string.Join('|', new[] { "RuntimeIdentifier", "Uri", "Sha256", "Size", "ExtractTo" }.Select(name => asset.Element(name)?.Value ?? "")))
            .ToList();
    }

    private static string? BindingVersion(string project)
    {
        return Project(project).Descendants("PackageReference")
            .SingleOrDefault(reference => (string?)reference.Attribute("Include") == BINDING_PACKAGE)
            ?.Attribute("Version")?.Value;
    }

    private static XDocument Project(string name)
    {
        return XDocument.Load(Path.Combine(AiRoot(), name, $"{name}.csproj"));
    }

    private static string AiRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, $"{SOLUTION_FILE} above the test folder");
        return Path.Combine(directory!.FullName, "AI");
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion
}
