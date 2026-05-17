using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OutWit.Common.Plugins.Abstractions;
using OutWit.Common.Plugins.Abstractions.Attributes;
using OutWit.Controller.Render.Adapters;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Utils;
using OutWit.Controller.Render.Variables;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render;

/// <summary>
/// WitEngine controller module for distributed rendering via Blender CLI.
/// Provides activities for splitting render jobs, rendering frames/tiles, and collecting results.
/// </summary>
[WitPluginManifest(ControllerBuildInfo.NAME, Version = ControllerBuildInfo.VERSION)]
[WitPluginDependency("Variables", MinimumVersion = "1.0.0")]
public class WitControllerRenderModule : WitPluginBase, IWitControllerNode, IWitControllerHost
{
    #region Initialization

    public override void Initialize(IServiceCollection services)
    {
        // Register a no-op IWitBlobService if none is provided by the host.
        services.TryAddSingleton<IWitBlobService, NullBlobService>();

        // Activities
        services.AddActivityAdapter<WitActivityRenderSplit, WitActivityAdapterRenderSplit>();
        services.AddActivityAdapter<WitActivityRenderSplitTiles, WitActivityAdapterRenderSplitTiles>();
        services.AddActivityAdapter<WitActivityRenderFrame, WitActivityAdapterRenderFrame>();
        services.AddActivityAdapter<WitActivityRenderFrameCycles, WitActivityAdapterRenderFrameCycles>();
        services.AddActivityAdapter<WitActivityRenderFrameEevee, WitActivityAdapterRenderFrameEevee>();
        services.AddActivityAdapter<WitActivityRenderFrameGreasePencil, WitActivityAdapterRenderFrameGreasePencil>();
        services.AddActivityAdapter<WitActivityRenderCollect, WitActivityAdapterRenderCollect>();
        services.AddActivityAdapter<WitActivityRenderCollectStill, WitActivityAdapterRenderCollectStill>();
        services.AddActivityAdapter<WitActivityRenderCollectTiles, WitActivityAdapterRenderCollectTiles>();
        services.AddActivityAdapter<WitActivityRenderBuildBlend, WitActivityAdapterRenderBuildBlend>();
        services.AddActivityAdapter<WitActivityRenderBuildBlendFromRefs, WitActivityAdapterRenderBuildBlendFromRefs>();
        services.AddActivityAdapter<WitActivityRenderEncodeVideo, WitActivityAdapterRenderEncodeVideo>();
        services.AddActivityAdapter<WitActivityRenderBlenderVersion, WitActivityAdapterRenderBlenderVersion>();
        services.AddActivityAdapter<WitActivityRenderPreflight, WitActivityAdapterRenderPreflight>();
        services.AddActivityAdapter<WitActivityRenderPreflightFrames, WitActivityAdapterRenderPreflightFrames>();
        services.AddActivityAdapter<WitActivityRenderPreflightStillTiled, WitActivityAdapterRenderPreflightStillTiled>();
        services.AddActivityAdapter<WitActivityRenderPreflightVideo, WitActivityAdapterRenderPreflightVideo>();
        services.AddActivityAdapter<WitActivityRenderRuntimeDiagnostics, WitActivityAdapterRenderRuntimeDiagnostics>();
        services.AddActivityAdapter<WitActivityRenderValidateBlend, WitActivityAdapterRenderValidateBlend>();

        // Variables
        services.AddVariable<WitVariableRenderOptions>();
        services.AddVariable<WitVariableTileOptions>();
        services.AddVariable<WitVariableVideoOptions>();
        services.AddVariable<WitVariableRenderScene>();
        services.AddVariable<WitVariableRenderSceneRef>();
        services.AddVariable<WitVariableRenderTask>();
        services.AddVariable<WitVariableRenderResult>();
        services.AddVariable<WitVariableRenderPreflight>();
        services.AddVariable<WitVariableRenderPreflightFrames>();
        services.AddVariable<WitVariableRenderPreflightStillTiled>();
        services.AddVariable<WitVariableRenderPreflightVideo>();
        services.AddVariable<WitVariableRenderRuntimeDiagnostics>();
        services.AddCollection<WitVariableRenderTaskCollection>();
        services.AddCollection<WitVariableRenderResultCollection>();
    }

    #endregion
}
