using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

[Activity("Schwarz.IsConverged")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzIsConverged : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{State}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzIsConverged activity)
            return false;

        return base.Is(activity, tolerance)
               && State.Check(activity.State);
    }

    protected override WitActivitySchwarzIsConverged InnerClone()
    {
        return new WitActivitySchwarzIsConverged
        {
            State = State?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? State { get; init; }

    #endregion
}
