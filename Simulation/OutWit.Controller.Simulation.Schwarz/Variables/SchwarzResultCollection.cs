using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Schwarz.Variables;

/// <summary>
/// Collection of Schwarz wave results — arrives in completion order; consumers re-key by SubdomainIndex.
/// </summary>
[Variable("SchwarzResultCollection")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzResultCollection : WitCollection<SchwarzResultData?>, IWitVariableFactory<WitVariableSchwarzResultCollection>
{
    #region Constructors

    public WitVariableSchwarzResultCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzResultCollection(string name, IReadOnlyList<SchwarzResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableSchwarzResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (SchwarzResultData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableSchwarzResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzResultCollection Create(string name)
    {
        return new WitVariableSchwarzResultCollection(name);
    }

    #endregion
}
