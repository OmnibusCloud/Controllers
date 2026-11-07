using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Variables;

/// <summary>
/// Script variable carrying one variant's FoamResultData - what Foam.Run
/// returns for one task. The collection form is what a chunk yields; the
/// single-value form is the transformer's return type.
/// </summary>
[Variable("FoamResult")]
[MemoryPackable]
public sealed partial class WitVariableFoamResult : WitVariable<FoamResultData?>, IWitVariableFactory<WitVariableFoamResult>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableFoamResult(string name)
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
    public WitVariableFoamResult(string name, FoamResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableFoamResult variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableFoamResult Clone()
    {
        return new WitVariableFoamResult(Name, GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableFoamResult Create(string name)
    {
        return new WitVariableFoamResult(name);
    }

    #endregion
}
