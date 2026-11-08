using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.AI.Corpus.Tests;

/// <summary>
/// The module: it carries its plugin manifest, initializes without error, every activity is
/// named under the Corpus prefix, and the consumer-side NuGet targets stage the module under
/// its own folder — a targets file copied from another controller once staged Corpus into
/// vision.module, and the server never saw a Corpus archive.
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
        Assert.That(services.Count, Is.GreaterThan(10), "variables, collections and adapters are registered");
    }

    [Test]
    public void EveryActivityIsNamedUnderTheCorpusPrefixTest()
    {
        var activities = typeof(WitActivityCorpusProcessShard).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttribute<ActivityAttribute>() != null)
            .ToList();

        Assert.That(activities, Has.Count.EqualTo(7));
        Assert.That(activities.Select(type => type.GetCustomAttribute<ActivityAttribute>()!.Type), Is.All.StartsWith("Corpus."));
    }

    [Test]
    public void ConsumerTargetsStageTheModuleUnderItsOwnFolderTest()
    {
        var root = SolutionRoot();
        if (root == null)
            Assert.Ignore("Solution root not found");

        var targets = File.ReadAllText(Path.Combine(root, "AI", "OutWit.Controller.AI.Corpus", "build", "OutWit.Controller.AI.Corpus.targets"));
        var folders = System.Text.RegularExpressions.Regex.Matches(targets, @"@Controllers\\([a-z]+)\.module").Select(match => match.Groups[1].Value).Distinct().ToList();

        Assert.That(folders, Is.EqualTo(new[] { "corpus" }), "the consumer targets must stage into corpus.module and nothing else");
    }

    private static string? SolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
