using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Controller.Simulation.Schwarz.Adapters;
using OutWit.Controller.Simulation.Schwarz.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz;

/// <summary>
/// Plugin entry point of the Schwarz controller: registers the solve's script
/// vocabulary — five variables (options, plan, round state, task, result), the
/// two wave collections, and the nine Schwarz.* activities paired with their
/// adapters. Implements both host and node contracts, so one assembly serves
/// both sides: the server runs the orchestration steps, compute nodes run only
/// Schwarz.SolveSubdomain.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerSchwarzModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>
    /// Registers the Schwarz vocabulary with the engine's service collection.
    /// Script-facing names come from the [Variable]/[Activity] attributes on
    /// the registered types, not from this method.
    /// </summary>
    /// <param name="services">Engine service collection the plugin populates.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableSchwarzOptions>();
        services.AddVariable<WitVariableSchwarzPlan>();
        services.AddVariable<WitVariableSchwarzRound>();
        services.AddVariable<WitVariableSchwarzTask>();
        services.AddVariable<WitVariableSchwarzResult>();

        services.AddCollection<WitVariableSchwarzTaskCollection>();
        services.AddCollection<WitVariableSchwarzResultCollection>();

        services.AddActivityAdapter<WitActivitySchwarzDecompose, WitActivityAdapterSchwarzDecompose>();
        services.AddActivityAdapter<WitActivitySchwarzInitRound, WitActivityAdapterSchwarzInitRound>();
        services.AddActivityAdapter<WitActivitySchwarzRoundBudget, WitActivityAdapterSchwarzRoundBudget>();
        services.AddActivityAdapter<WitActivitySchwarzMakeTasks, WitActivityAdapterSchwarzMakeTasks>();
        services.AddActivityAdapter<WitActivitySchwarzMakeFinalTasks, WitActivityAdapterSchwarzMakeFinalTasks>();
        services.AddActivityAdapter<WitActivitySchwarzSolveSubdomain, WitActivityAdapterSchwarzSolveSubdomain>();
        services.AddActivityAdapter<WitActivitySchwarzAdvance, WitActivityAdapterSchwarzAdvance>();
        services.AddActivityAdapter<WitActivitySchwarzIsConverged, WitActivityAdapterSchwarzIsConverged>();
        services.AddActivityAdapter<WitActivitySchwarzAssemble, WitActivityAdapterSchwarzAssemble>();
    }
}
