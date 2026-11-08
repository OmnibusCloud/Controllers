using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus;

/// <summary>
/// The Corpus controller: public-corpus processing at CPU scale. The node fetches its shard
/// from the public origin itself (Pattern U), streams it through deterministic stages and
/// returns the cleaned text, its signatures and its statistics through the blob subsystem.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerCorpusModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>Registers the Corpus variables, collections and activity adapters.</summary>
    /// <param name="services">The service collection of the engine host or node.</param>
    public override void Initialize(IServiceCollection services)
    {
        // CO-1 spike: the stage libraries (the WARC/WET reader first) land before any activity;
        // variables and adapters arrive with ProcessShard in CO-2.
    }
}
