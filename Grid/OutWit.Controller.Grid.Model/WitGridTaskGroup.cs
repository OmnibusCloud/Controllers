using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OutWit.Common.Abstract;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Model;

public class WitGridTaskGroup : IEnumerable<WitGridTask>
{
    #region Fields

    private readonly List<WitGridTask> m_tasks = new ();

    #endregion

    #region Constructors

    public WitGridTaskGroup(IWitEngineActivityNode node)
    {
        Node = node;
    }

    #endregion

    #region Functions

    internal void Add(WitGridTask task)
    {
        m_tasks.Add(task);
        TotalWork += task.Work;
        Eta = TimeSpan.FromSeconds(TotalWork / Rate);
    }

    #endregion

    #region IEnumerable

    public IEnumerator<WitGridTask> GetEnumerator()
    {
        return m_tasks.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    #endregion

    #region Properties

    public IWitEngineActivityNode Node { get; }
    
    public double TotalWork { get; private set; }
    
    public TimeSpan Eta { get; private set; }
    
    public int Count => m_tasks.Count;

    public double Rate => Math.Max(Node.BenchmarkResult.Rate, ModelBase.DEFAULT_TOLERANCE);

    #endregion
}
