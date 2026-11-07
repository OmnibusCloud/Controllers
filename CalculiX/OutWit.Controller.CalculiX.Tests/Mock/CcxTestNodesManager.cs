using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Tests.Mock;

/// <summary>
/// Two in-process "nodes" over the single WitEngineNodeSdk instance - the
/// Matrices/Grid/Sweep mock pattern: real task building and serialization
/// paths, no real network distribution. A test can ask for the node's job to
/// be cancelled a while after its work arrives, the way a user's cancel
/// reaches a running solve.
/// </summary>
internal sealed class CcxTestNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;

    #endregion

    #region Constructors

    public CcxTestNodesManager(IWitEngineNode node)
    {
        m_node = node;
        CompatibleNodes =
        [
            new CcxTestActivityNode(),
            new CcxTestActivityNode()
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
        ScheduleCancel(jobId);
        return m_node.Process(jobId, activity, pool, returnVariables);
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(
        Guid nodeId,
        Guid jobId,
        IReadOnlyList<WitNodeTaskRequest> requests,
        bool canRunInParallelOnClient)
    {
        ScheduleCancel(jobId);

        var allVariables = new List<IWitVariable>();
        IWitProcessingStatus? lastStatus = null;

        foreach (var request in requests)
        {
            var (status, variables) = await m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
            lastStatus = status;
            allVariables.AddRange(variables);

            if (status.Result != WitProcessingResult.Completed)
                return (status, allVariables);
        }

        return (lastStatus ?? throw new InvalidOperationException("No requests provided"), allVariables);
    }

    #endregion

    #region Tools

    private void ScheduleCancel(Guid jobId)
    {
        if (CancelAfter is not { } delay)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            m_node.Cancel(jobId);
        });
    }

    #endregion

    #region Properties

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    /// <summary>When set, the node's job is cancelled this long after its work arrives.</summary>
    public TimeSpan? CancelAfter { get; set; }

    #endregion
}
