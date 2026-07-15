using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerVerifyModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    public override void Initialize(IServiceCollection services)
    {
        // Skeleton: variable/activity registrations arrive with the first
        // activity (the sandboxed ExecuteBatch primitive).
    }
}
