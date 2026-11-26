using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The run's in-process nodes with liars among them: a liar does the work honestly (it is the same
/// sandbox) and then returns what <see cref="Lie"/> makes of every result package it made, or,
/// with <see cref="Twice"/>, answers the first package of a share twice: once for its own task and
/// once in place of the second package's result, which it leaves without an answer. Which nodes
/// lie is <see cref="Liars"/>: none, the first, or all of them. A lie of null leaves the package as
/// it was. The tests see the nodes as the server does: by what they return, never by who they are;
/// the documents are then held to name none.
/// </summary>
internal sealed class SmtLyingNodesManager : IWitNodesManager
{
    #region Constants

    /// <summary>The nodes of the rig: enough for two copies on different machines and a dispute on three.</summary>
    public const int NODES = 3;

    #endregion

    #region Fields

    private readonly SmtTestNodesManager m_inner;
    private readonly List<(string Doubled, string Robbed)> m_doubled = [];
    private int m_lies;

    #endregion

    #region Constructors

    /// <summary>Makes the nodes.</summary>
    /// <param name="node">The in-process node every one of them runs on.</param>
    public SmtLyingNodesManager(IWitEngineNode node)
    {
        m_inner = new SmtTestNodesManager(node, NODES);
    }

    #endregion

    #region IWitNodesManager

    /// <inheritdoc />
    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options)
        where TActivity : IWitActivity
    {
        return m_inner.GetCompatibleNodes<TActivity>(options);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
    {
        return m_inner.GetCompatibleNodes(activityType, options);
    }

    /// <inheritdoc />
    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity, IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
    {
        var (status, variables) = await m_inner.Process(nodeId, jobId, activity, pool, returnVariables);
        await ReturnedAsync(nodeId, variables);
        return (status, variables);
    }

    /// <inheritdoc />
    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId, IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
    {
        var (status, variables) = await m_inner.ProcessBatch(nodeId, jobId, requests, canRunInParallelOnClient);
        await ReturnedAsync(nodeId, variables);
        return (status, variables);
    }

    #endregion

    #region Tools

    /// <summary>Puts the liar's word in place of every result package a liar returns.</summary>
    private async Task ReturnedAsync(Guid nodeId, IReadOnlyList<IWitVariable> variables)
    {
        if (!IsLiar(nodeId))
            return;

        await TwiceAsync(variables);

        var lie = Lie;
        if (lie == null)
            return;

        foreach (var variable in variables)
        {
            if (variable.Value is not SmtResultPackageData honest)
                continue;

            var forged = await lie(honest);
            if (forged == null)
                continue;

            Interlocked.Increment(ref m_lies);
            variable.SetValue(forged);
        }
    }

    /// <summary>
    /// The first package of a share answered twice: what <see cref="Twice"/> makes of its result
    /// goes back under the second package's task, in place of that package's own result. A share
    /// of one package has nothing to rob.
    /// </summary>
    private async Task TwiceAsync(IReadOnlyList<IWitVariable> variables)
    {
        var twice = Twice;
        var results = variables.Where(variable => variable.Value is SmtResultPackageData { FaultCode.Length: 0 }).ToList();
        if (twice == null || results.Count < 2)
            return;

        var doubled = (SmtResultPackageData)results[0].Value!;
        var robbed = (SmtResultPackageData)results[1].Value!;
        var second = await twice(doubled);
        if (second == null)
            return;

        lock (m_doubled)
            m_doubled.Add((doubled.Ticket, robbed.Ticket));

        Interlocked.Increment(ref m_lies);
        results[1].SetValue(second);
    }

    private bool IsLiar(Guid nodeId)
    {
        return Liars switch
        {
            SmtLiars.First => nodeId == m_inner.CompatibleNodes[0].NodeId,
            SmtLiars.All => true,
            _ => false
        };
    }

    #endregion

    #region Properties

    /// <summary>What a liar returns in place of an honest result package; null for an honest rig, and a lie that gives null leaves the package as it was.</summary>
    public Func<SmtResultPackageData, Task<SmtResultPackageData?>>? Lie { get; set; }

    /// <summary>
    /// What a liar returns under the task of the second package of a share, made of the first
    /// package's result: a second answer of the first package; null for a rig that does not lie so,
    /// and a lie that gives null leaves the share as it was.
    /// </summary>
    public Func<SmtResultPackageData, Task<SmtResultPackageData?>>? Twice { get; set; }

    /// <summary>The tickets of the packages a liar answered twice, and of those it left without an answer for it, since the list was cleared.</summary>
    public IReadOnlyList<(string Doubled, string Robbed)> Doubled
    {
        get
        {
            lock (m_doubled)
                return m_doubled.ToList();
        }
    }

    /// <summary>Which nodes lie.</summary>
    public SmtLiars Liars { get; set; } = SmtLiars.First;

    /// <summary>The result packages the liars replaced since the count was reset; setting it clears <see cref="Doubled"/> as well.</summary>
    public int Lies
    {
        get => Volatile.Read(ref m_lies);
        set
        {
            Volatile.Write(ref m_lies, value);
            lock (m_doubled)
                m_doubled.Clear();
        }
    }

    /// <summary>Shares given to a node since the manager was made.</summary>
    public int Shares => m_inner.Shares;

    #endregion
}
