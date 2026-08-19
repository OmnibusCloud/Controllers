using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Activities;

/// <summary>
/// Validates a visualization package against its output options: the package reference and attachments,
/// the runtime requirement, the hardened-parsed state (proxy allowlist, programmable-pipeline rejection, file
/// references, views, timeline), and the frame selection. Host-side; downloads only the state, never an
/// attachment. Returns the validation report ParaView.Split turns into tasks.
/// </summary>
[Activity("ParaView.Validate")]
[MemoryPackable]
public sealed partial class WitActivityParaViewValidate : WitActivityFunction
{
    #region Functions

    /// <inheritdoc />
    protected override string InnerString()
    {
        return $"ParaView.Validate({Scene}, {Options})";
    }

    #endregion

    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityParaViewValidate other)
            return false;

        return base.Is(modelBase, tolerance);
    }

    /// <inheritdoc />
    protected override WitActivityParaViewValidate InnerClone()
    {
        return new WitActivityParaViewValidate
        {
            Scene = Scene,
            Options = Options
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Blob-backed package reference.
    /// </summary>
    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    /// <summary>
    /// Output options.
    /// </summary>
    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion
}
