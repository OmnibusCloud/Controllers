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
        : this(node, node.BenchmarkResult.Rate)
    {
    }

    /// <summary>
    /// A group whose planning rate differs from the node's own benchmark rate: the allocator
    /// hands an unmeasured node the slowest measured rate, so an unknown machine is never
    /// assumed to be the fastest one.
    /// </summary>
    /// <param name="node">The node the group is planned for.</param>
    /// <param name="rate">The rate the plan assumes for it (work units per second).</param>
    public WitGridTaskGroup(IWitEngineActivityNode node, double rate)
    {
        Node = node;
        Rate = Math.Max(rate, ModelBase.DEFAULT_TOLERANCE);
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

    public double Rate { get; }

    #endregion
}
