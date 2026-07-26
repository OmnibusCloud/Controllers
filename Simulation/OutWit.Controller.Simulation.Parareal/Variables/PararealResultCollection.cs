using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Controller.Simulation.Model;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Data.Collections;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Variables;

[Variable("PararealResultCollection")]
[MemoryPackable]
public sealed partial class WitVariablePararealResultCollection : WitCollection<PararealResultData?>, IWitVariableFactory<WitVariablePararealResultCollection>
{
    #region Constructors

    public WitVariablePararealResultCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealResultCollection(string name, IReadOnlyList<PararealResultData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealResultCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariablePararealResultCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (PararealResultData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariablePararealResultCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealResultCollection Create(string name)
    {
        return new WitVariablePararealResultCollection(name);
    }

    #endregion
}
