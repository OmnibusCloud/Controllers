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
/// ordered frame set. From 0.3.0 it also COMPOSES a scene from bare data (ParaView.Compose, one node
/// task per job through Grid.Delegate) into the same package reference the chain consumes. From
/// 0.5.0 the bundled animation scripts render in BATCHES (ParaView.SplitBatched +
/// ParaView.RenderFrameBatch): several outputs per pvpython process, so process startup — the
/// dominant cost of a one-frame task — is paid once per chunk.
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
        services.AddActivityAdapter<WitActivityParaViewSplitBatched, WitActivityAdapterParaViewSplitBatched>();
        services.AddActivityAdapter<WitActivityParaViewRenderFrameBatch, WitActivityAdapterParaViewRenderFrameBatch>();
        services.AddActivityAdapter<WitActivityParaViewCompose, WitActivityAdapterParaViewCompose>();
        services.AddActivityAdapter<WitActivityParaViewCollect, WitActivityAdapterParaViewCollect>();
        services.AddActivityAdapter<WitActivityParaViewCollectStill, WitActivityAdapterParaViewCollectStill>();

        // Variables
        services.AddVariable<WitVariableParaViewSceneRef>();
        services.AddVariable<WitVariableParaViewDataScene>();
        services.AddVariable<WitVariableParaViewOutputOptions>();
        services.AddVariable<WitVariableParaViewValidationReport>();
        services.AddVariable<WitVariableParaViewRenderTask>();
        services.AddVariable<WitVariableParaViewRenderResult>();
        services.AddVariable<WitVariableParaViewRenderTaskBatch>();
        services.AddVariable<WitVariableParaViewRenderResultBatch>();
        services.AddCollection<WitVariableParaViewRenderTaskCollection>();
        services.AddCollection<WitVariableParaViewRenderResultCollection>();
        services.AddCollection<WitVariableParaViewRenderTaskBatchCollection>();
        services.AddCollection<WitVariableParaViewRenderResultBatchCollection>();
    }

    #endregion
}
