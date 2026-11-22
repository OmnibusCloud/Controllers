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
/// returns the cleaned text, its signatures and its statistics through the blob subsystem. The
/// tasks and the results travel as blobs too: what the script's variables hold of a round is a
/// bounded number of references, whatever the manifest's length.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerCorpusModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>Registers the Corpus variables, collections and activity adapters.</summary>
    /// <param name="services">The service collection of the engine host or node.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableCorpusTaskPackage>();
        services.AddVariable<WitVariableCorpusResultPackage>();
        services.AddVariable<WitVariableCorpusOptions>();
        services.AddVariable<WitVariableCorpusPreflight>();
        services.AddVariable<WitVariableCorpusRuntimeDiagnostics>();

        services.AddCollection<WitVariableCorpusTaskPackageCollection>();
        services.AddCollection<WitVariableCorpusResultPackageCollection>();

        services.AddActivityAdapter<WitActivityCorpusProcessPackage, WitActivityAdapterCorpusProcessPackage>();
        services.AddActivityAdapter<WitActivityCorpusSplit, WitActivityAdapterCorpusSplit>();
        services.AddActivityAdapter<WitActivityCorpusPreflight, WitActivityAdapterCorpusPreflight>();
        services.AddActivityAdapter<WitActivityCorpusRepairs, WitActivityAdapterCorpusRepairs>();
        services.AddActivityAdapter<WitActivityCorpusDisputes, WitActivityAdapterCorpusDisputes>();
        services.AddActivityAdapter<WitActivityCorpusCollect, WitActivityAdapterCorpusCollect>();
        services.AddActivityAdapter<WitActivityCorpusSamplingRate, WitActivityAdapterCorpusSamplingRate>();
        services.AddActivityAdapter<WitActivityCorpusRepairRate, WitActivityAdapterCorpusRepairRate>();
        services.AddActivityAdapter<WitActivityCorpusIntegrity, WitActivityAdapterCorpusIntegrity>();
        services.AddActivityAdapter<WitActivityCorpusDedup, WitActivityAdapterCorpusDedup>();
        services.AddActivityAdapter<WitActivityCorpusRuntimeDiagnostics, WitActivityAdapterCorpusRuntimeDiagnostics>();
        services.AddActivityAdapter<WitActivityCorpusCapabilities, WitActivityAdapterCorpusCapabilities>();
    }
}
