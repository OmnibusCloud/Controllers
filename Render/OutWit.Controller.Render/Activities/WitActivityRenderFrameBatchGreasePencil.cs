using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>Renders a chunk of Grease Pencil frames/tiles in a single Blender process. See <see cref="WitActivityRenderFrameBatch"/>.</summary>
[Activity("Render.FrameBatch.GreasePencil")]
[CanRunInParallelOnClientAttribute(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderFrameBatchGreasePencil : WitActivityFunction, IRenderFrameActivity
{
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Task { get; init; }

    protected override string InnerString() => $"Render.FrameBatch.GreasePencil({Task})";

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderFrameBatchGreasePencil other)
            return false;

        return base.Is(modelBase, tolerance) && Task.Check(other.Task);
    }

    protected override WitActivityRenderFrameBatchGreasePencil InnerClone()
    {
        return new WitActivityRenderFrameBatchGreasePencil { Task = Task?.Clone() as IWitParameter };
    }
}
