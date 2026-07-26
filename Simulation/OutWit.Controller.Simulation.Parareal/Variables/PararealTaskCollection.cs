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

[Variable("PararealTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariablePararealTaskCollection : WitCollection<PararealTaskData?>, IWitVariableFactory<WitVariablePararealTaskCollection>
{
    #region Constructors

    public WitVariablePararealTaskCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariablePararealTaskCollection(string name, IReadOnlyList<PararealTaskData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariablePararealTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariablePararealTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (PararealTaskData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariablePararealTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariablePararealTaskCollection Create(string name)
    {
        return new WitVariablePararealTaskCollection(name);
    }

    #endregion
}
