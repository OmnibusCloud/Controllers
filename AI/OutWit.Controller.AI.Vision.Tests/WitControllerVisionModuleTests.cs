using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Vision;

namespace OutWit.Controller.AI.Vision.Tests;

[TestFixture]
public sealed class WitControllerVisionModuleTests
{
    #region Module Tests

    [Test]
    public void ModuleCarriesPluginManifestAttributeTest()
    {
        var manifest = typeof(WitControllerVisionModule)
            .GetCustomAttributes(inherit: false)
            .SingleOrDefault(attribute => attribute.GetType().Name == "WitPluginManifestAttribute");

        Assert.That(manifest, Is.Not.Null);
    }

    [Test]
    public void ModuleInitializeRegistersWithoutErrorTest()
    {
        var services = new ServiceCollection();

        Assert.DoesNotThrow(() => new WitControllerVisionModule().Initialize(services));
    }

    #endregion

    #region Manifest Tests

    [Test]
    public void StagedManifestMatchesControllerIdentityTest()
    {
        var manifestPath = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "@Controllers", "vision.module", "controller.json");

        Assert.That(File.Exists(manifestPath), Is.True,
            $"controller.json is not staged at '{manifestPath}' — the module staging pipeline is broken.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));

        Assert.That(manifest.RootElement.GetProperty("name").GetString(), Is.EqualTo("Vision"));
        Assert.That(manifest.RootElement.GetProperty("version").GetString(), Is.EqualTo("0.1.0"));
        Assert.That(manifest.RootElement.GetProperty("assemblyName").GetString(), Is.EqualTo("OutWit.Controller.AI.Vision"));
    }

    #endregion
}
