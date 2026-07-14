using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Simulation.Schwarz.Activities;
using OutWit.Controller.Simulation.Schwarz.Adapters;
using OutWit.Controller.Simulation.Schwarz.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerSchwarzModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
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
