using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Simulation.Parareal.Activities;
using OutWit.Controller.Simulation.Parareal.Adapters;
using OutWit.Controller.Simulation.Parareal.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerPararealModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariablePararealOptions>();
        services.AddVariable<WitVariablePararealPlan>();
        services.AddVariable<WitVariablePararealState>();
        services.AddVariable<WitVariablePararealTask>();
        services.AddVariable<WitVariablePararealResult>();

        services.AddCollection<WitVariablePararealTaskCollection>();
        services.AddCollection<WitVariablePararealResultCollection>();

        services.AddActivityAdapter<WitActivityPararealSlice, WitActivityAdapterPararealSlice>();
        services.AddActivityAdapter<WitActivityPararealIterationBudget, WitActivityAdapterPararealIterationBudget>();
        services.AddActivityAdapter<WitActivityPararealInit, WitActivityAdapterPararealInit>();
        services.AddActivityAdapter<WitActivityPararealMakeTasks, WitActivityAdapterPararealMakeTasks>();
        services.AddActivityAdapter<WitActivityPararealPropagate, WitActivityAdapterPararealPropagate>();
        services.AddActivityAdapter<WitActivityPararealCorrect, WitActivityAdapterPararealCorrect>();
        services.AddActivityAdapter<WitActivityPararealIsConverged, WitActivityAdapterPararealIsConverged>();
        services.AddActivityAdapter<WitActivityPararealMakeSnapshotTasks, WitActivityAdapterPararealMakeSnapshotTasks>();
        services.AddActivityAdapter<WitActivityPararealCollect, WitActivityAdapterPararealCollect>();
    }
}
