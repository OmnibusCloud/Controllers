using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The run's in-process nodes with liars among them: a liar does the work honestly (it is the same
/// sandbox) and then returns what <see cref="Lie"/> makes of every result package it made. Which
/// nodes lie is <see cref="Liars"/>: none, the first, or all of them. A lie of null leaves the
/// package as it was. The tests see the nodes as the server does: by what they return, never by
/// who they are; the documents are then held to name none.
/// </summary>
internal sealed class SmtLyingNodesManager : IWitNodesManager
{
    #region Constants

    /// <summary>The nodes of the rig: enough for two copies on different machines and a dispute on three.</summary>
    public const int NODES = 3;

    #endregion

    #region Fields

    private readonly SmtTestNodesManager m_inner;
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
        var lie = Lie;
        if (lie == null || !IsLiar(nodeId))
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

    /// <summary>Which nodes lie.</summary>
    public SmtLiars Liars { get; set; } = SmtLiars.First;

    /// <summary>The result packages the liars replaced since the count was reset.</summary>
    public int Lies
    {
        get => Volatile.Read(ref m_lies);
        set => Volatile.Write(ref m_lies, value);
    }

    /// <summary>Shares given to a node since the manager was made.</summary>
    public int Shares => m_inner.Shares;

    #endregion
}
