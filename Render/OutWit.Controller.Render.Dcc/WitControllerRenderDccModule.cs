using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Render.Dcc.Activities;
using OutWit.Controller.Render.Dcc.Adapters;
using OutWit.Controller.Render.Dcc.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
[WitPluginDependency("Render", MinimumVersion = "1.15.0")]
public class WitControllerRenderDccModule : WitPluginBase, IWitControllerHost
{
    #region Initialization

    public override void Initialize(IServiceCollection services)
    {
        services.AddActivityAdapter<WitActivityRenderBuildBlendFromDccScene, WitActivityAdapterRenderBuildBlendFromDccScene>();
        services.AddActivityAdapter<WitActivityRenderClearScene, WitActivityAdapterRenderClearScene>();
        services.AddVariable<WitVariableDccScene>();
    }

    #endregion
}
