using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Three in-process "nodes" over one WitEngineNodeSdk instance — real task building and
/// serialization paths, no real network distribution (the Grid/Matrices mock pattern). A test may
/// make one node dishonest: <see cref="Tamper"/> sees every shard result a node returns.
/// </summary>
internal sealed class CorpusTestNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;

    #endregion

    #region Constructors

    public CorpusTestNodesManager(IWitEngineNode node)
    {
        m_node = node;
        CompatibleNodes =
        [
            new CorpusTestActivityNode(node),
            new CorpusTestActivityNode(node),
            new CorpusTestActivityNode(node)
        ];
    }

    #endregion

    #region IWitNodesManager

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options)
        where TActivity : IWitActivity
    {
        return Task.FromResult(CompatibleNodes);
    }

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
    {
        return Task.FromResult(CompatibleNodes);
    }

    public Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(
        Guid nodeId,
        Guid jobId,
        IWitActivity activity,
        IWitVariablesCollection pool,
        IReadOnlyList<string> returnVariables)
    {
        return TamperAsync(nodeId, m_node.Process(jobId, activity, pool, returnVariables));
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(
        Guid nodeId,
        Guid jobId,
        IReadOnlyList<WitNodeTaskRequest> requests,
        bool canRunInParallelOnClient)
    {
        var allVariables = new List<IWitVariable>();
        IWitProcessingStatus? lastStatus = null;

        foreach (var request in requests)
        {
            var (status, variables) = await TamperAsync(nodeId, m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables));
            lastStatus = status;
            allVariables.AddRange(variables);

            if (status.Result == WitProcessingResult.Failed)
                return (status, allVariables);
        }

        return (lastStatus ?? throw new InvalidOperationException("No requests provided"), allVariables);
    }

    #endregion

    #region Tools

    private async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> TamperAsync(Guid nodeId, Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> processing)
    {
        var (status, variables) = await processing;
        if (Tamper != null)
        {
            foreach (var variable in variables)
            {
                if (variable.Value is CorpusResultData result)
                    Tamper(nodeId, result);
            }
        }

        return (status, variables);
    }

    #endregion

    #region Properties

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    /// <summary>Changes the shard results a node returns (a dishonest node), or null for honest nodes.</summary>
    public Action<Guid, CorpusResultData>? Tamper { get; set; }

    #endregion
}
