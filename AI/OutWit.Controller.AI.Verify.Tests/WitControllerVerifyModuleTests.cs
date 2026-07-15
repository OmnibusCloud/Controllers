using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Verify;

namespace OutWit.Controller.AI.Verify.Tests;

[TestFixture]
public sealed class WitControllerVerifyModuleTests
{
    #region Module Tests

    [Test]
    public void ModuleCarriesPluginManifestAttributeTest()
    {
        var manifest = typeof(WitControllerVerifyModule)
            .GetCustomAttributes(inherit: false)
            .SingleOrDefault(attribute => attribute.GetType().Name == "WitPluginManifestAttribute");

        Assert.That(manifest, Is.Not.Null);
    }

    [Test]
    public void ModuleInitializeRegistersWithoutErrorTest()
    {
        var services = new ServiceCollection();

        Assert.DoesNotThrow(() => new WitControllerVerifyModule().Initialize(services));
    }

    #endregion

    #region Manifest Tests

    [Test]
    public void StagedManifestMatchesControllerIdentityTest()
    {
        var manifestPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "@Controllers", "verify.module", "controller.json");

        Assert.That(File.Exists(manifestPath), Is.True,
            $"controller.json is not staged at '{manifestPath}' — the module staging pipeline is broken.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        Assert.That(manifest.RootElement.GetProperty("name").GetString(), Is.EqualTo("Verify"));
        Assert.That(manifest.RootElement.GetProperty("version").GetString(), Is.EqualTo("0.1.0"));
        Assert.That(manifest.RootElement.GetProperty("assemblyName").GetString(), Is.EqualTo("OutWit.Controller.AI.Verify"));
    }

    #endregion
}
