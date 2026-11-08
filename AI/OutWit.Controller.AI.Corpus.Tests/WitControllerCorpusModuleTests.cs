using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions.Attributes;

namespace OutWit.Controller.AI.Corpus.Tests;

/// <summary>
/// The module skeleton: it carries its plugin manifest and initializes without error.
/// </summary>
[TestFixture]
public sealed class WitControllerCorpusModuleTests
{
    [Test]
    public void ModuleCarriesPluginManifestAttributeTest()
    {
        var attribute = typeof(WitControllerCorpusModule).GetCustomAttributes(typeof(WitPluginManifestAttribute), false)
            .OfType<WitPluginManifestAttribute>()
            .SingleOrDefault();

        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute!.Name, Is.EqualTo("Corpus"));
    }

    [Test]
    public void ModuleInitializeRegistersWithoutErrorTest()
    {
        var services = new ServiceCollection();
        var module = new WitControllerCorpusModule();

        Assert.DoesNotThrow(() => module.Initialize(services));
    }
}
