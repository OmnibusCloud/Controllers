using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Validates that a .blend blob can be opened by the Blender runtime available in the current controller package.
/// </summary>
[Activity("Render.ValidateBlend")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[RequiresResources(RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderValidateBlend : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.ValidateBlend({Scene})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderValidateBlend && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderValidateBlend InnerClone()
    {
        return new WitActivityRenderValidateBlend
        {
            Scene = Scene
        };
    }

    #endregion
}
