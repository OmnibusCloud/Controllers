using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Activities;

[Activity("Schwarz.Decompose")]
[MemoryPackable]
public sealed partial class WitActivitySchwarzDecompose : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"{Model}, {Options}";
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivitySchwarzDecompose activity)
            return false;

        return base.Is(activity, tolerance)
               && Model.Check(activity.Model)
               && Options.Check(activity.Options);
    }

    protected override WitActivitySchwarzDecompose InnerClone()
    {
        return new WitActivitySchwarzDecompose
        {
            Model = Model?.Clone() as IWitReference,
            Options = Options?.Clone() as IWitReference
        };
    }

    #endregion

    #region Properties

    [MemoryPackAllowSerialize]
    public IWitReference? Model { get; init; }

    [MemoryPackAllowSerialize]
    public IWitReference? Options { get; init; }

    #endregion
}
