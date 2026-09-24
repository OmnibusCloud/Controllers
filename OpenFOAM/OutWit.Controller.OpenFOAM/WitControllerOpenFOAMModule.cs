using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.OpenFOAM.Activities;
using OutWit.Controller.OpenFOAM.Adapters;
using OutWit.Controller.OpenFOAM.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM;

/// <summary>
/// Plugin entry point of the OpenFOAM controller: registers the run's script
/// vocabulary - the per-variant task and result variables with their
/// collections, and the Foam.Run activity. Implements both host and node
/// contracts: nodes run the case, and being loadable host-side is what lets
/// the host-only Sweep orchestration controller declare its dependency on
/// this module honestly.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerOpenFOAMModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>
    /// Registers the OpenFOAM vocabulary with the engine's service collection.
    /// Script-facing names come from the [Variable]/[Activity] attributes on
    /// the registered types, not from this method.
    /// </summary>
    /// <param name="services">Engine service collection the plugin populates.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableFoamTask>();
        services.AddVariable<WitVariableFoamResult>();

        services.AddCollection<WitVariableFoamTaskCollection>();
        services.AddCollection<WitVariableFoamResultCollection>();

        services.AddActivityAdapter<WitActivityFoamRun, WitActivityAdapterFoamRun>();
    }
}
