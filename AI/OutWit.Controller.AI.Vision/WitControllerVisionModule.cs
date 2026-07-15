using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Vision;

[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public sealed class WitControllerVisionModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    public override void Initialize(IServiceCollection services)
    {
        // Skeleton: variable/activity registrations arrive with the first
        // activity (annotation extraction). The Render controller dependency
        // is declared only when Vision starts consuming Render result
        // variables, to keep the module load surface minimal until then.
    }
}
