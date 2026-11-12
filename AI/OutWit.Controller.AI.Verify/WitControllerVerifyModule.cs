using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.AI.Verify.Activities;
using OutWit.Controller.AI.Verify.Adapters;
using OutWit.Controller.AI.Verify.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerVerifyModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    /// <summary>Registers the Verify variables, collections and activity adapters.</summary>
    /// <param name="services">The service collection of the engine host or node.</param>
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableVerifyTask>();
        services.AddVariable<WitVariableVerifyResult>();
        services.AddVariable<WitVariableVerifyTaskPackage>();
        services.AddVariable<WitVariableVerifyResultPackage>();
        services.AddVariable<WitVariableVerifyOptions>();
        services.AddVariable<WitVariableVerifyPreflight>();
        services.AddVariable<WitVariableVerifyRuntimeDiagnostics>();

        services.AddCollection<WitVariableVerifyTaskPackageCollection>();
        services.AddCollection<WitVariableVerifyResultPackageCollection>();

        services.AddActivityAdapter<WitActivityVerifyExecutePackage, WitActivityAdapterVerifyExecutePackage>();
        services.AddActivityAdapter<WitActivityVerifyExecute, WitActivityAdapterVerifyExecute>();
        services.AddActivityAdapter<WitActivityVerifySplit, WitActivityAdapterVerifySplit>();
        services.AddActivityAdapter<WitActivityVerifyCollect, WitActivityAdapterVerifyCollect>();
        services.AddActivityAdapter<WitActivityVerifyProvenance, WitActivityAdapterVerifyProvenance>();
        services.AddActivityAdapter<WitActivityVerifyCollectOutputs, WitActivityAdapterVerifyCollectOutputs>();
        services.AddActivityAdapter<WitActivityVerifyMakeCheckTasks, WitActivityAdapterVerifyMakeCheckTasks>();
        services.AddActivityAdapter<WitActivityVerifySamplingRate, WitActivityAdapterVerifySamplingRate>();
        services.AddActivityAdapter<WitActivityVerifyIntegrity, WitActivityAdapterVerifyIntegrity>();
        services.AddActivityAdapter<WitActivityVerifyPreflight, WitActivityAdapterVerifyPreflight>();
        services.AddActivityAdapter<WitActivityVerifyRuntimeDiagnostics, WitActivityAdapterVerifyRuntimeDiagnostics>();
        services.AddActivityAdapter<WitActivityVerifyCapabilities, WitActivityAdapterVerifyCapabilities>();
    }
}
