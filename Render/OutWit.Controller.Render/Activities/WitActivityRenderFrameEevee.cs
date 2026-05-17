using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Renders a single Eevee frame or tile from a .blend file using Blender CLI.
/// </summary>
[Activity("Render.Frame.Eevee")]
[CanRunInParallelOnClientAttribute(false)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(MinRamMb = 4096, MinTempStorageMb = 10240)]
[MemoryPackable]
public sealed partial class WitActivityRenderFrameEevee : WitActivityFunction, IRenderFrameActivity
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Task { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.Frame.Eevee({Task})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderFrameEevee)
            return false;

        return base.Is(modelBase, tolerance)
               && Task.Check(((WitActivityRenderFrameEevee)modelBase).Task);
    }

    protected override WitActivityRenderFrameEevee InnerClone()
    {
        return new WitActivityRenderFrameEevee
        {
            Task = Task?.Clone() as IWitParameter
        };
    }

    #endregion
}
