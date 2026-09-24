using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Sweep.Activities;
using OutWit.Controller.Sweep.Adapters;
using OutWit.Controller.Sweep.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep;

/// <summary>
/// Plugin entry point of the Sweep controller: the host-side orchestration of
/// a parameter study - plan, chunked tasks, harvest, finish - for every solver
/// family the host knows (CalculiX decks, OpenFOAM cases), with one activity
/// vocabulary and one manifest. HOST-ONLY by construction (IWitControllerHost
/// alone): the server never delivers this module to compute nodes, which is
/// exactly what lets it declare its dependencies on Grid and Special honestly
/// - those are enforced by the host loader only. The families' node modules
/// are dependencies because their task and result types ride through the
/// sweep's scripts.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
[WitPluginDependency("Grid", MinimumVersion = "1.0.0")]
[WitPluginDependency("Special", MinimumVersion = "1.0.0")]
[WitPluginDependency("CalculiX", MinimumVersion = "1.1.0")]
[WitPluginDependency("OpenFOAM", MinimumVersion = "1.0.0")]
public sealed class WitControllerSweepModule : WitPluginBase, IWitControllerHost
{
    /// <summary>
    /// Registers the Sweep vocabulary with the engine's service collection.
    /// Script-facing names come from the [Variable]/[Activity] attributes on
    /// the registered types, not from this method.
    /// </summary>
    /// <param name="services">Engine service collection the plugin populates.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableSweepOptions>();
        services.AddVariable<WitVariableSweepPlan>();
        services.AddVariable<WitVariableSweepState>();

        services.AddActivityAdapter<WitActivitySweepPlan, WitActivityAdapterSweepPlan>();
        services.AddActivityAdapter<WitActivitySweepInitState, WitActivityAdapterSweepInitState>();
        services.AddActivityAdapter<WitActivitySweepChunkCount, WitActivityAdapterSweepChunkCount>();
        services.AddActivityAdapter<WitActivitySweepMakeChunk, WitActivityAdapterSweepMakeChunk>();
        services.AddActivityAdapter<WitActivitySweepHarvest, WitActivityAdapterSweepHarvest>();
        services.AddActivityAdapter<WitActivitySweepFinish, WitActivityAdapterSweepFinish>();
    }
}
