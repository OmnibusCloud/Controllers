using System.Xml.Linq;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The binding guard of Verify's copy of the wasm host: the version it was built against is the
/// <c>Wasmtime</c> PackageReference of the controller and the reference the compiler recorded,
/// the binding of this process passes, and any other version (or none) makes the host
/// unavailable with a message naming both versions.
/// </summary>
[TestFixture]
public sealed class WasmHostBindingTests
{
    #region Constants

    private const string BINDING_NAME = "Wasmtime.Dotnet";
    private const string CONTROLLER_PROJECT = "OutWit.Controller.AI.Verify";

    #endregion

    #region Pin Tests

    [Test]
    public void TheBuiltAgainstVersionIsTheControllersPackageReferenceTest()
    {
        var project = Path.Combine(WasmHostRepository.AiRoot(), CONTROLLER_PROJECT, $"{CONTROLLER_PROJECT}.csproj");

        Assert.That(WasmHostRepository.WasmtimeVersion(XDocument.Load(project)), Is.EqualTo(WasmHostBinding.BUILT_AGAINST));
    }

    [Test]
    public void TheBuiltAgainstVersionIsTheReferenceTheCompilerRecordedTest()
    {
        var reference = typeof(VerifyWasmSandbox).Assembly.GetReferencedAssemblies().SingleOrDefault(name => name.Name == BINDING_NAME);

        Assert.That(reference?.Version, Is.Not.Null, $"the controller references {BINDING_NAME}");
        Assert.That(WasmHostBinding.Check(reference!.Version, WasmHostBinding.BUILT_AGAINST), Is.Null,
            $"compiled against {reference.Version}, the constant says {WasmHostBinding.BUILT_AGAINST}");
    }

    #endregion

    #region Guard Tests

    [Test]
    public void TheBindingOfThisProcessPassesTheGuardTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WasmHostBinding.Problem, Is.Null);
            Assert.That(WasmHostBinding.Loaded, Is.EqualTo(new Version(WasmHostBinding.BUILT_AGAINST + ".0")));
            Assert.DoesNotThrow(WasmHostBinding.ThrowIfUnavailable);
        });
    }

    [Test]
    public void TheSameVersionPassesWithOrWithoutItsFourthPartTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WasmHostBinding.Check(new Version(48, 0, 2, 0), "48.0.2"), Is.Null);
            Assert.That(WasmHostBinding.Check(new Version(48, 0, 2), "48.0.2"), Is.Null);
            Assert.That(WasmHostBinding.Check(new Version(48, 0, 2, 0), "48.0.2.0"), Is.Null);
        });
    }

    [Test]
    public void AnotherVersionMakesTheHostUnavailableNamingBothTest()
    {
        var patch = WasmHostBinding.Check(new Version(48, 0, 3, 0), "48.0.2");
        var major = WasmHostBinding.Check(new Version(50, 0, 1, 0), "48.0.2");

        Assert.Multiple(() =>
        {
            Assert.That(patch, Does.Contain("Wasmtime.Dotnet 48.0.3.0").And.Contain("built against 48.0.2").And.Contain("the sandbox stays off"));
            Assert.That(major, Does.Contain("Wasmtime.Dotnet 50.0.1.0").And.Contain("built against 48.0.2"));
            Assert.That(WasmHostBinding.Check(new Version(48, 0, 2, 1), "48.0.2"), Is.Not.Null, "the fourth part counts when it is not 0");
        });
    }

    [Test]
    public void ABindingThatCannotLoadMakesTheHostUnavailableTest()
    {
        Assert.That(WasmHostBinding.Check(null, "48.0.2"), Does.Contain("could not be loaded").And.Contain("built against 48.0.2"));
    }

    #endregion
}
