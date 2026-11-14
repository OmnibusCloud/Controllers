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
    private const string NATIVE_VERSION_PIN = "WASMTIME_NATIVE_VERSION";

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
            Assert.That(references.Select(reference => reference.Name), Does.Contain(VERIFY_PROJECT));
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
            Assert.That(carriers.Select(carrier => carrier.Name), Does.Contain(VERIFY_PROJECT));
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

    #endregion
}
