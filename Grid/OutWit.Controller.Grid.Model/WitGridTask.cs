using System;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Model;

public class WitGridTask : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
    {
        if (modelBase is not WitGridTask task)
            return false;

        return Work.Is(task.Work) &&
               ReplicaOf.Is(task.ReplicaOf) &&
               Variables.Is(task.Variables) &&
               Activity.Check(task.Activity);

    }

    public override WitGridTask Clone()
    {
        return new WitGridTask
        {
            Work = Work,
            ReplicaOf = ReplicaOf,
            Variables = (IWitVariablesCollection)Variables.Clone(),
            Activity = (IWitActivity)Activity.Clone()
        };
    }

    #endregion

    #region Properties

    [ToString]
    public double Work { get; init; }

    /// <summary>
    /// The item this task is one execution of, when that item runs more than once; null when it
    /// runs once. Tasks with the same value never share a machine. Stays on the host: a node sees
    /// a copy exactly as it sees any other task.
    /// </summary>
    [ToString]
    public int? ReplicaOf { get; init; }

    public IWitVariablesCollection Variables { get; init; }
    
    [ToString]
    public IWitActivity Activity { get; init; }

    #endregion
    

}