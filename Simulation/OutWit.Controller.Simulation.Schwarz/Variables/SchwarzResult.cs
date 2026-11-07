using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Math.Simulation;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Math.Simulation.Model.Schwarz;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Script variable carrying one subdomain's SchwarzResultData — what a compute
/// node returns from Schwarz.SolveSubdomain. Scripts rarely name it on its own:
/// Grid.ForEach gathers a whole wave into a SchwarzResultCollection, which is
/// what Schwarz.Advance and Schwarz.Assemble consume.
/// </summary>
[Variable("SchwarzResult")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzResult : WitVariable<SchwarzResultData?>, IWitVariableFactory<WitVariableSchwarzResult>
{
    #region Constructors

    /// <summary>
    /// Creates the variable with no payload yet — the form used when the
    /// script declares it ahead of first assignment.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    public WitVariableSchwarzResult(string name)
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
    public WitVariableSchwarzResult(string name, SchwarzResultData? value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzResult variable)
            return false;

        return base.Is(modelBase, tolerance)
               && Value.Check(variable.Value);
    }

    public override WitVariableSchwarzResult Clone()
    {
        return new WitVariableSchwarzResult(Name, (SchwarzResultData?)GetValue()?.Clone());
    }

    #endregion

    #region IWitVariableFactory

    /// <summary>
    /// Factory hook the engine calls when the script declares a variable of
    /// this type.
    /// </summary>
    /// <param name="name">Script name of the variable.</param>
    /// <returns>An empty variable awaiting its first assignment.</returns>
    public static WitVariableSchwarzResult Create(string name)
    {
        return new WitVariableSchwarzResult(name);
    }

    #endregion
}
