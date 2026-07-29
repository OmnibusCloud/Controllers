using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.CalculiX.Activities;
using OutWit.Controller.CalculiX.Adapters;
using OutWit.Controller.CalculiX.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX;

/// <summary>
/// Plugin entry point of the CalculiX controller: registers the solve's
/// script vocabulary — the per-variant task and result variables with their
/// collections, and the Ccx.Solve activity. Implements both host and node
/// contracts: nodes run the solve, and being loadable host-side is what lets
/// the host-only Sweep orchestration controller declare its dependency on
/// this module honestly.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerCalculiXModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>
    /// Registers the CalculiX vocabulary with the engine's service collection.
    /// Script-facing names come from the [Variable]/[Activity] attributes on
    /// the registered types, not from this method.
    /// </summary>
    /// <param name="services">Engine service collection the plugin populates.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableCcxTask>();
        services.AddVariable<WitVariableCcxResult>();

        services.AddCollection<WitVariableCcxTaskCollection>();
        services.AddCollection<WitVariableCcxResultCollection>();

        services.AddActivityAdapter<WitActivityCcxSolve, WitActivityAdapterCcxSolve>();
    }
}
