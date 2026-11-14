using System.Xml.Linq;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The repository-level pins of the wasm host. The cloud client loads every controller into one
/// shared load context, where the binding assembly loads once, so every AI project that references
/// the <c>Wasmtime</c> package references the same version, and every controller that compiles the
/// shared host is built against the version its guard names. The native libraries a controller
/// ships are the pinned patch of the binding's engine line, one per platform the asset tool builds.
/// Read from the project files and the asset tool's pins: nothing is downloaded.
/// </summary>
[TestFixture]
public sealed class WasmHostPinsTests
{
    #region Constants

    private const string VERIFY_PROJECT = "OutWit.Controller.AI.Verify.csproj";
    private const string CORPUS_PROJECT = "OutWit.Controller.AI.Corpus.csproj";
    private const string NATIVE_VERSION_PIN = "WASMTIME_NATIVE_VERSION";
    private const string DATA_ASSET_ITEM = "ControllerDataAsset";
    private const string RESOLVE_TASK = "ResolveControllerAssetsTask";

    #endregion

    #region Binding Tests

    [Test]
    public void EveryAiProjectReferencesOneWasmtimeVersionTest()
    {
        var references = WasmHostRepository.AiProjects()
            .Select(project => (Name: Path.GetFileName(project.Path), Version: WasmHostRepository.WasmtimeVersion(project.Project)))
            .Where(reference => reference.Version != null)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(references.Select(reference => reference.Name), Does.Contain(VERIFY_PROJECT).And.Contain(CORPUS_PROJECT));
            Assert.That(references.Select(reference => reference.Version).Distinct(), Has.Exactly(1).Items,
                string.Join("\n", references.Select(reference => $"{reference.Name}: {reference.Version}")));
        });
    }

    [Test]
    public void EveryControllerThatCarriesTheHostIsBuiltAgainstItsGuardsVersionTest()
    {
        var carriers = WasmHostRepository.AiProjects()
            .Where(project => WasmHostRepository.CarriesTheSharedHost(project.Project))
            .Select(project => (Name: Path.GetFileName(project.Path), Version: WasmHostRepository.WasmtimeVersion(project.Project)))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(carriers.Select(carrier => carrier.Name), Does.Contain(VERIFY_PROJECT).And.Contain(CORPUS_PROJECT), "Verify and Corpus each carry their own copy of the host");
            foreach (var (name, version) in carriers)
                Assert.That(version, Is.EqualTo(WasmHostBinding.BUILT_AGAINST), $"{name} compiles the shared host, whose guard names {WasmHostBinding.BUILT_AGAINST}");
        });
    }

    #endregion

    #region Native Tests

    [Test]
    public void TheNativesPinIsAPatchOfTheBindingsEngineLineTest()
    {
        var native = Version.Parse(WasmHostRepository.Pin(NATIVE_VERSION_PIN));
        var binding = Version.Parse(WasmHostBinding.BUILT_AGAINST);

        Assert.Multiple(() =>
        {
            Assert.That(native.Major, Is.EqualTo(binding.Major), "the natives are the binding's engine line");
            Assert.That(native.Minor, Is.EqualTo(binding.Minor));
            Assert.That(native.Build, Is.GreaterThanOrEqualTo(binding.Build), "the newest patch of the line, never an older one");
        });
    }

    [Test]
    public void TheShippedNativesAreThePinnedVersionForEveryBuiltPlatformTest()
    {
        var pinned = WasmHostRepository.Pin(NATIVE_VERSION_PIN);
        var built = WasmHostRepository.NativesScriptRids();
        var verify = WasmHostRepository.AiProjects().Single(project => Path.GetFileName(project.Path) == VERIFY_PROJECT);

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.EquivalentTo(new[] { "win-x64", "linux-x64", "osx-arm64" }), "natives.sh lays out the three node platforms");
            Assert.That(VerifyEngineVersion.SHIPPED_NATIVES.Values, Is.All.EqualTo(pinned), "the libraries Verify recognises are the pinned version");
            Assert.That(VerifyEngineVersion.SHIPPED_NATIVES, Has.Count.EqualTo(built.Count), "one recognised library a platform");
            Assert.That(WasmHostRepository.ShippedNativeRids(verify.Project), Is.EquivalentTo(built), "Verify ships a native for every platform natives.sh builds");

            // A controller that ships natives of its own ships them for the same platforms.
            foreach (var (path, project) in WasmHostRepository.AiProjects())
            {
                var rids = WasmHostRepository.ShippedNativeRids(project);
                if (rids.Count > 0)
                    Assert.That(rids, Is.EquivalentTo(built), Path.GetFileName(path));
            }
        });
    }

    [Test]
    public void EveryControllerShipsTheSameNativeBytesTest()
    {
        // The binding loads once per process in the client's shared context, and the natives with
        // it: the controllers that carry the host ship the same files, whatever release their
        // URIs name, so whichever loads first, the engine is the same.
        var verify = WasmHostRepository.ShippedNatives(WasmHostRepository.AiProjects().Single(project => Path.GetFileName(project.Path) == VERIFY_PROJECT).Project);
        var shippers = WasmHostRepository.AiProjects()
            .Select(project => (Name: Path.GetFileName(project.Path), Natives: WasmHostRepository.ShippedNatives(project.Project)))
            .Where(shipper => shipper.Natives.Count > 0)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shippers.Select(shipper => shipper.Name), Does.Contain(CORPUS_PROJECT), "Corpus ships natives of its own");
            foreach (var (name, natives) in shippers)
                Assert.That(natives, Is.EquivalentTo(verify), $"{name}: the same platforms, hashes and sizes as Verify's");
        });
    }

    #endregion

    #region Packaging Tests

    [Test]
    public void EveryControllerWithDataAssetsResolvesThemInItsConsumerTargetsTest()
    {
        // A package stages only its own files into the consumer's module folder; the data assets
        // (natives, wasm modules) reach it only through the consumer targets' resolve step. Corpus
        // 0.2.0 declared its assets but kept the Tier-1 targets: the server packed it without
        // natives and without its extractor, and in the client's shared load context, where its
        // binding loaded first, Verify's P/Invokes found no wasmtime either.
        var withAssets = WasmHostRepository.AiProjects()
            .Where(project => project.Project.Descendants().Any(element => element.Name.LocalName == DATA_ASSET_ITEM))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(withAssets.Select(project => Path.GetFileName(project.Path)), Does.Contain(VERIFY_PROJECT).And.Contain(CORPUS_PROJECT));
            foreach (var (path, _) in withAssets)
            {
                var targets = Path.Combine(Path.GetDirectoryName(path)!, "build", Path.GetFileNameWithoutExtension(path) + ".targets");
                Assert.That(File.Exists(targets), Is.True, $"{Path.GetFileName(path)} has consumer targets");
                // The task as an element: a comment that names it (the Tier-1 template says "no
                // ResolveControllerAssetsTask step") must not pass for it.
                if (File.Exists(targets))
                    Assert.That(XDocument.Load(targets).Descendants().Any(element => element.Name.LocalName == RESOLVE_TASK), Is.True,
                        $"{Path.GetFileName(targets)} resolves the data assets its project declares");
            }
        });
    }

    #endregion
}
