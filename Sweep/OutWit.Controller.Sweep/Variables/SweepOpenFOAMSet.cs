using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Sweep.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Sweep.Variables;

/// <summary>
/// Script variable carrying the cases of an OpenFOAM case set as submitted by
/// the client - the second job input of the SweepOpenFOAMSet script.
/// </summary>
[Variable("SweepOpenFOAMSet")]
[MemoryPackable]
public sealed partial class WitVariableSweepOpenFOAMSet : WitVariable<SweepOpenFOAMSetData?>, IWitVariableFactory<WitVariableSweepOpenFOAMSet>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSweepOpenFOAMSet(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Deserialization constructor: rehydrates name and payload together when
    /// the variable crosses the wire.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <param name="value">Deserialized payload; null when the variable is unset.</param>
    [MemoryPackConstructor]
    public WitVariableSweepOpenFOAMSet(string name, SweepOpenFOAMSetData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSweepOpenFOAMSet variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSweepOpenFOAMSet Clone()
    {
        return new WitVariableSweepOpenFOAMSet(Name, GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSweepOpenFOAMSet Create(string name)
    {
        return new WitVariableSweepOpenFOAMSet(name);
    }

    #endregion
}
