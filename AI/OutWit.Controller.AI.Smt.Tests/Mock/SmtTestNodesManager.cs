using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// In-process "nodes" over one node SDK: real task building and serialization, no network. Each
/// node has an id of its own, so copies of a package (<c>Grid.Replicas</c>) go to different ones.
/// A share whose activity allows it is started all at once, as the client starts it.
/// </summary>
internal sealed class SmtTestNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;

    private int m_shares;

    #endregion

    #region Constructors

    /// <summary>Makes the nodes.</summary>
    /// <param name="node">The in-process node every one of them runs on.</param>
    /// <param name="nodes">How many.</param>
    public SmtTestNodesManager(IWitEngineNode node, int nodes)
    {
        m_node = node;
        CompatibleNodes = Enumerable.Range(0, nodes).Select(_ => (IWitEngineActivityNode)new SmtTestActivityNode()).ToList();
    }

    #endregion

    #region IWitNodesManager

    /// <inheritdoc />
    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options)
        where TActivity : IWitActivity
    {
        return Task.FromResult(CompatibleNodes);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
    {
        return Task.FromResult(CompatibleNodes);
    }

    /// <inheritdoc />
    public Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity, IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
    {
        return m_node.Process(jobId, activity, pool, returnVariables);
    }

    /// <inheritdoc />
    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId, IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
    {
        var allVariables = new List<IWitVariable>();
        IWitProcessingStatus? lastStatus = null;

        Interlocked.Increment(ref m_shares);

        if (canRunInParallelOnClient)
        {
            var executions = await Task.WhenAll(requests.Select(request => m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables)));
            foreach (var (status, variables) in executions)
            {
                lastStatus = status;
                allVariables.AddRange(variables);

                if (status.Result == WitProcessingResult.Failed)
                    return (status, allVariables);
            }

            return (lastStatus ?? throw new InvalidOperationException("No requests provided"), allVariables);
        }

        foreach (var request in requests)
        {
            var (status, variables) = await m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
            lastStatus = status;
            allVariables.AddRange(variables);

            if (status.Result == WitProcessingResult.Failed)
                return (status, allVariables);
        }

        return (lastStatus ?? throw new InvalidOperationException("No requests provided"), allVariables);
    }

    #endregion

    #region Properties

    /// <summary>The nodes.</summary>
    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    /// <summary>Shares given to a node since the manager was made.</summary>
    public int Shares => Volatile.Read(ref m_shares);

    #endregion
}
