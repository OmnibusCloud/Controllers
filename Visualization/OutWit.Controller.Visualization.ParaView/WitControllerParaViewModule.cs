using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Visualization.ParaView.Activities;
using OutWit.Controller.Visualization.ParaView.Adapters;
using OutWit.Controller.Visualization.ParaView.Collections;
using OutWit.Controller.Visualization.ParaView.Utils;
using OutWit.Controller.Visualization.ParaView.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView;

/// <summary>
/// WitEngine controller module for headless scientific visualization with ParaView: validates
/// visualization packages, splits them into per-timestep tasks with per-task attachment subsets,
/// renders tasks through the controller-owned pvpython runner on worker nodes, and collects the
/// ordered frame set.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
// Grid is a runtime dependency of the bundled scripts (Grid.ForEach dispatches ParaView.RenderFrame),
// declared in the csproj as <ControllerDependency Include="Grid"> for the manifest. It is NOT a
// [WitPluginDependency] here: that attribute is enforced by both the host- and the node-side plugin
// loaders, and Grid is host-only — declaring it would make every node fail validation (see the
// Render module for the same note).
public class WitControllerParaViewModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    #region Initialization

    /// <inheritdoc />
    public override void Initialize(IServiceCollection services)
    {
        // Register a no-op IWitBlobService if none is provided by the host.
        services.TryAddSingleton<IWitBlobService, NullBlobService>();

        // Temp storage: the host (cloud client) registers an IWitTempStorage rooted at the operator-
        // configured temp directory; fall back to the system temp directory otherwise.
        services.TryAddSingleton<IWitTempStorage>(_ => new WitTempStorageDefault(Path.GetTempPath()));

        // Activities
        services.AddActivityAdapter<WitActivityParaViewValidate, WitActivityAdapterParaViewValidate>();
        services.AddActivityAdapter<WitActivityParaViewSplit, WitActivityAdapterParaViewSplit>();
        services.AddActivityAdapter<WitActivityParaViewRenderFrame, WitActivityAdapterParaViewRenderFrame>();
        services.AddActivityAdapter<WitActivityParaViewCollect, WitActivityAdapterParaViewCollect>();
        services.AddActivityAdapter<WitActivityParaViewCollectStill, WitActivityAdapterParaViewCollectStill>();

        // Variables
        services.AddVariable<WitVariableParaViewSceneRef>();
        services.AddVariable<WitVariableParaViewOutputOptions>();
        services.AddVariable<WitVariableParaViewValidationReport>();
        services.AddVariable<WitVariableParaViewRenderTask>();
        services.AddVariable<WitVariableParaViewRenderResult>();
        services.AddCollection<WitVariableParaViewRenderTaskCollection>();
        services.AddCollection<WitVariableParaViewRenderResultCollection>();
    }

    #endregion
}
