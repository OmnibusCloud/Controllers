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
/// Collection of Schwarz wave tasks. Output of Schwarz.MakeTasks/MakeFinalTasks, input to Grid.ForEach.
/// </summary>
[Variable("SchwarzTaskCollection")]
[MemoryPackable]
public sealed partial class WitVariableSchwarzTaskCollection : WitCollection<SchwarzTaskData?>, IWitVariableFactory<WitVariableSchwarzTaskCollection>
{
    #region Constructors

    public WitVariableSchwarzTaskCollection(string name)
        : base(name)
    {
    }

    [MemoryPackConstructor]
    public WitVariableSchwarzTaskCollection(string name, IReadOnlyList<SchwarzTaskData?> value)
        : base(name, value)
    {
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitVariableSchwarzTaskCollection variable)
            return false;

        return base.Is(modelBase, tolerance)
               && GetValue().Is(variable.GetValue());
    }

    public override WitVariableSchwarzTaskCollection Clone()
    {
        var clonedItems = GetValue()?
            .Select(x => (SchwarzTaskData?)x?.Clone())
            .ToArray() ?? [];

        return new WitVariableSchwarzTaskCollection(Name, clonedItems);
    }

    #endregion

    #region IWitVariableFactory

    public static WitVariableSchwarzTaskCollection Create(string name)
    {
        return new WitVariableSchwarzTaskCollection(name);
    }

    #endregion
}
