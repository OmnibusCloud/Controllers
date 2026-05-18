using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Mock;

internal sealed class RenderTestNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;

    #endregion

    #region Constructors

    public RenderTestNodesManager(IWitEngineNode node)
    {
        m_node = node;
        CompatibleNodes = [new RenderTestActivityNode(node)];
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
        return m_node.Process(jobId, activity, pool, returnVariables);
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

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    #endregion
}
