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
               Variables.Is(task.Variables) &&
               Activity.Check(task.Activity);

    }

    public override WitGridTask Clone()
    {
        return new WitGridTask
        {
            Work = Work,
            Variables = (IWitVariablesCollection)Variables.Clone(),
            Activity = (IWitActivity)Activity.Clone()
        };
    }

    #endregion

    #region Properties

    [ToString]
    public double Work { get; init; }
    
    public IWitVariablesCollection Variables { get; init; }
    
    [ToString]
    public IWitActivity Activity { get; init; }

    #endregion
    

}