using Microsoft.Extensions.DependencyInjection;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Special.Activities;
using OutWit.Controller.Special.Adapters;
using OutWit.Controller.Special.Properties;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Special
{
    [WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
    [WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
    public class WitControllerSpecialModule : WitPluginBase, IWitControllerNode, IWitControllerHost
    {
        public override void Initialize(IServiceCollection services)
        {
            services.AddActivityAdapter<WitActivitySpecialBreak, WitActivityAdapterSpecialBreak>();
            services.AddActivityAdapter<WitActivitySpecialContinue, WitActivityAdapterSpecialContinue>();
            services.AddActivityAdapter<WitActivitySpecialDelayed, WitActivityAdapterSpecialDelayed>();
            services.AddActivityAdapter<WitActivitySpecialForEach, WitActivityAdapterSpecialForEach>();
            services.AddActivityAdapter<WitActivitySpecialIf, WitActivityAdapterSpecialIf>();
            services.AddActivityAdapter<WitActivitySpecialInvoke, WitActivityAdapterSpecialInvoke>();
            services.AddActivityAdapter<WitActivitySpecialLoop, WitActivityAdapterSpecialLoop>();
            services.AddActivityAdapter<WitActivitySpecialParallelForEach, WitActivityAdapterSpecialParallelForEach>();
            services.AddActivityAdapter<WitActivitySpecialParallelInvoke, WitActivityAdapterSpecialParallelInvoke>();
            services.AddActivityAdapter<WitActivitySpecialPause, WitActivityAdapterSpecialPause>();
            services.AddActivityAdapter<WitActivitySpecialReturn, WitActivityAdapterSpecialReturn>();
            services.AddActivityAdapter<WitActivitySpecialTimer, WitActivityAdapterSpecialTimer>();
            services.AddActivityAdapter<WitActivitySpecialTrace, WitActivityAdapterSpecialTrace>();
            services.AddActivityAdapter<WitActivitySpecialTransformForEach, WitActivityAdapterSpecialTransformForEach>();
            services.AddActivityAdapter<WitActivitySpecialZip, WitActivityAdapterSpecialZip>();

            services.AddResources<Resources>();
        }
    }
}
