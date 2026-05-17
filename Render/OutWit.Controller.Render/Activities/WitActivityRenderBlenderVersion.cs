using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Returns the Blender version string available in the current render controller runtime.
/// </summary>
[Activity("Render.BlenderVersion")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityRenderBlenderVersion : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return "Render.BlenderVersion()";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderBlenderVersion && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderBlenderVersion InnerClone()
    {
        return new WitActivityRenderBlenderVersion();
    }

    #endregion
}
