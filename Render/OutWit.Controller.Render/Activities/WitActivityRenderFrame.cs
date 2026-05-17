using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Renders a single frame or tile from a .blend file using Blender CLI.
/// Node-side activity: downloads .blend from blob storage, runs Blender, uploads result.
/// Accepts a single <see cref="Variables.RenderTaskData"/> containing all parameters.
/// </summary>
/// <remarks>
/// Resource requirements: Blender needs minimum 4 GB RAM and 10 GB temp storage.
/// GPU is NOT required — Blender can render on CPU. GPU device is selected at runtime.
/// Blender captures all CPU/GPU resources — parallel execution on one node is disabled.
/// </remarks>
[Activity("Render.Frame")]
[CanRunInParallelOnClientAttribute(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240)]
[MemoryPackable]
public sealed partial class WitActivityRenderFrame : WitActivityFunction, IRenderFrameActivity
{
    #region Properties

    /// <summary>
    /// Self-contained render task description (scene blob, frame, tile coords, options).
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Task { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.Frame({Task})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderFrame other)
            return false;

        return base.Is(modelBase, tolerance)
               && Task.Check(other.Task);
    }

    protected override WitActivityRenderFrame InnerClone()
    {
        return new WitActivityRenderFrame
        {
            Task = Task?.Clone() as IWitParameter
        };
    }

    #endregion
}
