using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>Renders a chunk of Eevee frames/tiles in a single Blender process. See <see cref="WitActivityRenderFrameBatch"/>.</summary>
[Activity("Render.FrameBatch.Eevee")]
[CanRunInParallelOnClientAttribute(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240, RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderFrameBatchEevee : WitActivityFunction, IRenderFrameActivity
{
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Task { get; init; }

    protected override string InnerString() => $"Render.FrameBatch.Eevee({Task})";

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderFrameBatchEevee other)
            return false;

        return base.Is(modelBase, tolerance) && Task.Check(other.Task);
    }

    protected override WitActivityRenderFrameBatchEevee InnerClone()
    {
        return new WitActivityRenderFrameBatchEevee { Task = Task?.Clone() as IWitParameter };
    }
}
