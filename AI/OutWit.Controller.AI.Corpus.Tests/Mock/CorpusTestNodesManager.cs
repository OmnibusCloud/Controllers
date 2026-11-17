using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Three in-process "nodes" over one WitEngineNodeSdk instance — real task building and
/// serialization paths, no real network distribution (the Grid/Matrices mock pattern). A test may
/// make one node dishonest: <see cref="Tamper"/> sees every shard result a node returns, and
/// <see cref="Twice"/> makes a node return a result once more than its tasks asked for. Both sit
/// where a node's answer reaches Grid, so Grid sees what a dishonest node would send it.
/// </summary>
internal sealed class CorpusTestNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;
    private int m_surplus;
    private int m_values;

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

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(
        Guid nodeId,
        Guid jobId,
        IWitActivity activity,
        IWitVariablesCollection pool,
        IReadOnlyList<string> returnVariables)
    {
        var (status, variables) = await m_node.Process(jobId, activity, pool, returnVariables);

        return (status, Returned(variables, Bend(nodeId, variables)));
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(
        Guid nodeId,
        Guid jobId,
        IReadOnlyList<WitNodeTaskRequest> requests,
        bool canRunInParallelOnClient)
    {
        var allVariables = new List<IWitVariable>();
        var again = new List<IWitVariable>();
        IWitProcessingStatus? lastStatus = null;

        foreach (var request in requests)
        {
            var (status, variables) = await m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
            again.AddRange(Bend(nodeId, variables));
            lastStatus = status;
            allVariables.AddRange(variables);

            if (status.Result == WitProcessingResult.Failed)
                return (status, Returned(allVariables, again));
        }

        return (lastStatus ?? throw new InvalidOperationException("No requests provided"), Returned(allVariables, again));
    }

    #endregion

    #region Tools

    /// <summary>Lets the test change what a node returns; gives back the values the node returns once more.</summary>
    private List<IWitVariable> Bend(Guid nodeId, IReadOnlyList<IWitVariable> variables)
    {
        var again = new List<IWitVariable>();
        foreach (var variable in variables)
        {
            if (variable.Value is not CorpusResultData result)
                continue;

            Tamper?.Invoke(nodeId, result);

            // The second value goes under the name of the first: the return variable its task asked for.
            if (Twice != null && Twice(nodeId, result))
                again.Add(new WitVariableCorpusResult(variable.Name, result.Clone()));
        }

        return again;
    }

    /// <summary>What the node hands to Grid for its call: the answers to its tasks, then the values nobody asked for.</summary>
    private IReadOnlyList<IWitVariable> Returned(IReadOnlyList<IWitVariable> variables, List<IWitVariable> again)
    {
        Interlocked.Add(ref m_values, variables.Count + again.Count);
        if (again.Count == 0)
            return variables;

        Interlocked.Add(ref m_surplus, again.Count);

        return variables.Concat(again).ToList();
    }

    #endregion

    #region Properties

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    /// <summary>Changes the shard results a node returns (a dishonest node), or null for honest nodes.</summary>
    public Action<Guid, CorpusResultData>? Tamper { get; set; }

    /// <summary>
    /// Makes a node answer more than it was asked: a result this says true for is returned once
    /// more, after every answer of the node's call. Null for nodes that return one value for each task.
    /// </summary>
    public Func<Guid, CorpusResultData, bool>? Twice { get; set; }

    /// <summary>Every value the nodes handed to Grid so far, the ones nobody asked for among them.</summary>
    public int ValuesReturned => Volatile.Read(ref m_values);

    /// <summary>The values the nodes handed to Grid beyond the ones their tasks asked for.</summary>
    public int SurplusReturned => Volatile.Read(ref m_surplus);

    #endregion
}
