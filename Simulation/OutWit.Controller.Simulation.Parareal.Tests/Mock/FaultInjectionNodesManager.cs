using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Simulation.Parareal.Tests.Mock;

/// <summary>
/// Three in-process "nodes" with DISTINCT ids where one designated victim fails
/// its first batch — exercises GridReassignment: the victim's tasks must be
/// re-packed onto the survivors and, because node activities are pure functions
/// over immutable blobs, the final result must be bitwise-identical.
/// </summary>
internal sealed class FaultInjectionNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;
    private readonly Guid m_victimId = Guid.NewGuid();
    private int m_remainingStrikes;

    #endregion

    #region Constructors

    public FaultInjectionNodesManager(IWitEngineNode node, int strikes = 1)
    {
        m_node = node;
        m_remainingStrikes = strikes;
        CompatibleNodes =
        [
            new FaultInjectionActivityNode(m_victimId),
            new FaultInjectionActivityNode(Guid.NewGuid()),
            new FaultInjectionActivityNode(Guid.NewGuid())
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
        ThrowIfVictimStrikes(nodeId);
        return m_node.Process(jobId, activity, pool, returnVariables);
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(
        Guid nodeId,
        Guid jobId,
        IReadOnlyList<WitNodeTaskRequest> requests,
        bool canRunInParallelOnClient)
    {
        ThrowIfVictimStrikes(nodeId);

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

    private void ThrowIfVictimStrikes(Guid nodeId)
    {
        if (nodeId == m_victimId && Interlocked.Decrement(ref m_remainingStrikes) >= 0)
            throw new InvalidOperationException("Injected node failure (fault-tolerance test).");
    }

    #endregion

    #region Properties

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    #endregion
}
