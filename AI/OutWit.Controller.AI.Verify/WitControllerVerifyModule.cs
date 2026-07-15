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
    public override void Initialize(IServiceCollection services)
    {
        services.AddVariable<WitVariableVerifyTask>();
        services.AddVariable<WitVariableVerifyResult>();
        services.AddVariable<WitVariableVerifyTaskBatch>();
        services.AddVariable<WitVariableVerifyResultBatch>();
        services.AddVariable<WitVariableVerifyOptions>();
        services.AddVariable<WitVariableVerifyPreflight>();
        services.AddVariable<WitVariableVerifyRuntimeDiagnostics>();

        services.AddCollection<WitVariableVerifyTaskBatchCollection>();
        services.AddCollection<WitVariableVerifyResultBatchCollection>();

        services.AddActivityAdapter<WitActivityVerifyExecuteBatch, WitActivityAdapterVerifyExecuteBatch>();
        services.AddActivityAdapter<WitActivityVerifyExecute, WitActivityAdapterVerifyExecute>();
        services.AddActivityAdapter<WitActivityVerifySplit, WitActivityAdapterVerifySplit>();
        services.AddActivityAdapter<WitActivityVerifyCollect, WitActivityAdapterVerifyCollect>();
        services.AddActivityAdapter<WitActivityVerifyPreflight, WitActivityAdapterVerifyPreflight>();
        services.AddActivityAdapter<WitActivityVerifyRuntimeDiagnostics, WitActivityAdapterVerifyRuntimeDiagnostics>();
    }
}
