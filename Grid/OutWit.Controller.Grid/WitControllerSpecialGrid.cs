using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Grid.Activities;
using OutWit.Controller.Grid.Adapters;
using OutWit.Controller.Grid.Properties;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid
{
    [WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
    public class WitControllerGridModule : WitPluginBase, IWitControllerHost
    {
        public override void Initialize(IServiceCollection services)
        {
            services.AddActivityAdapter<WitActivityGridForEach, WitActivityAdapterGridForEach>();

            services.AddResources<Resources>();
        }
    }
}
