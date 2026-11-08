using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.AI.Corpus.Activities;
using OutWit.Controller.AI.Corpus.Adapters;
using OutWit.Controller.AI.Corpus.Variables;
using OutWit.Engine.Data.Utils;
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
        services.AddVariable<WitVariableCorpusTask>();
        services.AddVariable<WitVariableCorpusResult>();
        services.AddVariable<WitVariableCorpusOptions>();
        services.AddVariable<WitVariableCorpusPreflight>();

        services.AddCollection<WitVariableCorpusTaskCollection>();
        services.AddCollection<WitVariableCorpusResultCollection>();

        services.AddActivityAdapter<WitActivityCorpusProcessShard, WitActivityAdapterCorpusProcessShard>();
        services.AddActivityAdapter<WitActivityCorpusSplit, WitActivityAdapterCorpusSplit>();
        services.AddActivityAdapter<WitActivityCorpusPreflight, WitActivityAdapterCorpusPreflight>();
        services.AddActivityAdapter<WitActivityCorpusCollect, WitActivityAdapterCorpusCollect>();
        services.AddActivityAdapter<WitActivityCorpusSample, WitActivityAdapterCorpusSample>();
        services.AddActivityAdapter<WitActivityCorpusIntegrity, WitActivityAdapterCorpusIntegrity>();
    }
}
