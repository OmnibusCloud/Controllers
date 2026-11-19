using MemoryPack;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// In-process "nodes" over one WitEngineNodeSdk instance — real task building and serialization
/// paths, no real network distribution (the Grid/Matrices mock pattern). A test may make one node
/// dishonest. What a node returns is an answer: a reference to a blob of results.
/// <see cref="Tamper"/> sees every shard result of every answer a node returns (the answer is
/// then written anew, as that node would have written it), <see cref="Rewrite"/> replaces a whole
/// answer, <see cref="Twice"/> makes a node return an answer once more than its tasks asked
/// for, and <see cref="Null"/> makes it return a value that is no answer under the name its task
/// asked for. All sit where a node's answer reaches Grid, so Grid sees what a dishonest node would send
/// it. The manager also measures what travels: the Corpus values a task carries to a node as
/// parameters and the ones that come back as return values, in the wire's own encoding.
/// </summary>
internal sealed class CorpusTestNodesManager : IWitNodesManager
{
    #region Fields

    private readonly IWitEngineNode m_node;
    private readonly IWitBlobService m_blobs;
    private int m_surplus;
    private int m_values;
    private long m_requests;
    private long m_requestBytes;
    private long m_largestRequestBytes;
    private long m_returnBytes;
    private long m_answerBytes;
    private long m_largestAnswerBytes;

    #endregion

    #region Constructors

    public CorpusTestNodesManager(IWitEngineNode node, IWitBlobService blobs, int nodes = 3)
    {
        m_node = node;
        m_blobs = blobs;
        CompatibleNodes = Enumerable.Range(0, nodes).Select(_ => (IWitEngineActivityNode)new CorpusTestActivityNode(node)).ToList();
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
        var again = new List<IWitVariable>();

        return (status, Returned(Bend(nodeId, variables, again), again));
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

        Sent(requests);

        if (StartTogether && canRunInParallelOnClient)
        {
            // As a client does: every task of the batch started together.
            var outcomes = await Task.WhenAll(requests.Select(request => m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables)));
            foreach (var (status, variables) in outcomes)
            {
                lastStatus = status;
                allVariables.AddRange(Bend(nodeId, variables, again));

                if (status.Result == WitProcessingResult.Failed)
                    return (status, Returned(allVariables, again));
            }
        }
        else
        {
            foreach (var request in requests)
            {
                var (status, variables) = await m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
                lastStatus = status;
                allVariables.AddRange(Bend(nodeId, variables, again));

                if (status.Result == WitProcessingResult.Failed)
                    return (status, Returned(allVariables, again));
            }
        }

        return (lastStatus ?? throw new InvalidOperationException("No requests provided"), Returned(allVariables, again));
    }

    #endregion

    #region Tools

    /// <summary>Lets the test change what a node returns; collects the answers the node returns once more.</summary>
    private List<IWitVariable> Bend(Guid nodeId, IReadOnlyList<IWitVariable> variables, List<IWitVariable> again)
    {
        var bent = new List<IWitVariable>(variables.Count);
        foreach (var variable in variables)
        {
            if (variable.Value is not CorpusResultPackageData answer)
            {
                bent.Add(variable);
                continue;
            }

            // A value of the right type and name that holds nothing: what the wire can carry.
            if (Null != null && Null(nodeId, answer))
            {
                bent.Add(new WitVariableCorpusResultPackage(variable.Name, null));
                continue;
            }

            if (Tamper != null && answer.ResultsBlobId != Guid.Empty)
            {
                // The node's results, changed and written as that node would have written them.
                var results = CorpusTestPackages.Results(m_blobs, answer);
                foreach (var result in results)
                    Tamper(nodeId, result);

                answer = CorpusTestPackages.Answer(m_blobs, answer.Ticket, results);
            }

            if (Rewrite != null)
                answer = Rewrite(nodeId, answer);

            Uploaded(answer);
            bent.Add(new WitVariableCorpusResultPackage(variable.Name, answer));

            // The second answer goes under the name of the first: the return variable its task asked for.
            if (Twice != null && Twice(nodeId, answer))
                again.Add(new WitVariableCorpusResultPackage(variable.Name, answer.Clone()));
        }

        return bent;
    }

    /// <summary>What the node hands to Grid for its call: the answers to its tasks, then the values nobody asked for.</summary>
    private IReadOnlyList<IWitVariable> Returned(IReadOnlyList<IWitVariable> variables, List<IWitVariable> again)
    {
        Interlocked.Add(ref m_values, variables.Count + again.Count);
        Interlocked.Add(ref m_returnBytes, variables.Concat(again).Sum(Measure));
        if (again.Count == 0)
            return variables;

        Interlocked.Add(ref m_surplus, again.Count);

        return variables.Concat(again).ToList();
    }

    private void Sent(IReadOnlyList<WitNodeTaskRequest> requests)
    {
        Interlocked.Add(ref m_requests, requests.Count);
        foreach (var request in requests)
        {
            var bytes = request.Pool.Sum(Measure);
            Interlocked.Add(ref m_requestBytes, bytes);
            Raise(ref m_largestRequestBytes, bytes);
        }
    }

    private void Uploaded(CorpusResultPackageData answer)
    {
        if (answer.ResultsBlobId == Guid.Empty)
            return;

        try
        {
            var bytes = new FileInfo(m_blobs.GetLocalPathAsync(answer.ResultsBlobId).GetAwaiter().GetResult()).Length;
            Interlocked.Add(ref m_answerBytes, bytes);
            Raise(ref m_largestAnswerBytes, bytes);
        }
        catch (FileNotFoundException)
        {
            // An answer a test made up, naming a blob nobody holds.
        }
    }

    private static void Raise(ref long highest, long value)
    {
        long seen;
        while (value > (seen = Volatile.Read(ref highest)) && Interlocked.CompareExchange(ref highest, value, seen) != seen)
        {
        }
    }

    /// <summary>The bytes of one Corpus value on the wire (the engine's own variables of a pool are not counted).</summary>
    private static long Measure(IWitVariable variable)
    {
        return variable.Value switch
        {
            CorpusTaskPackageData package => MemoryPackSerializer.Serialize(package).Length,
            CorpusResultPackageData answer => MemoryPackSerializer.Serialize(answer).Length,
            CorpusTaskData task => MemoryPackSerializer.Serialize(task).Length,
            CorpusResultData result => MemoryPackSerializer.Serialize(result).Length,
            _ => 0
        };
    }

    #endregion

    #region Properties

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    /// <summary>Changes the shard results a node returns (a dishonest node), or null for honest nodes.</summary>
    public Action<Guid, CorpusResultData>? Tamper { get; set; }

    /// <summary>Replaces an answer a node returns with another (a dishonest node), after <see cref="Tamper"/>; null for none.</summary>
    public Func<Guid, CorpusResultPackageData, CorpusResultPackageData>? Rewrite { get; set; }

    /// <summary>
    /// Makes a node answer more than it was asked: an answer this says true for is returned once
    /// more, after every answer of the node's call. Null for nodes that return one value for each task.
    /// </summary>
    public Func<Guid, CorpusResultPackageData, bool>? Twice { get; set; }

    /// <summary>
    /// Makes a node answer a task with nothing: an answer this says true for is replaced by a
    /// null value under the return variable its task asked for. It is asked of every answer of
    /// every node, before anything else is done to the answer. Null for nodes that answer.
    /// </summary>
    public Func<Guid, CorpusResultPackageData, bool>? Null { get; set; }

    /// <summary>Whether a node starts the tasks of a batch together, as a client does (false: one after another).</summary>
    public bool StartTogether { get; set; }

    /// <summary>Every value the nodes handed to Grid so far, the ones nobody asked for among them.</summary>
    public int ValuesReturned => Volatile.Read(ref m_values);

    /// <summary>The values the nodes handed to Grid beyond the ones their tasks asked for.</summary>
    public int SurplusReturned => Volatile.Read(ref m_surplus);

    /// <summary>The task requests sent to the nodes so far.</summary>
    public long Requests => Interlocked.Read(ref m_requests);

    /// <summary>The bytes of the Corpus values that travelled to the nodes as parameters of their tasks, in the wire's encoding.</summary>
    public long RequestBytes => Interlocked.Read(ref m_requestBytes);

    /// <summary>The most bytes of Corpus values one task carried to a node as its parameters.</summary>
    public long LargestRequestBytes => Interlocked.Read(ref m_largestRequestBytes);

    /// <summary>The bytes of the Corpus values that travelled back as return values, in the wire's encoding.</summary>
    public long ReturnBytes => Interlocked.Read(ref m_returnBytes);

    /// <summary>The bytes of every results blob the nodes uploaded.</summary>
    public long AnswerBlobBytes => Interlocked.Read(ref m_answerBytes);

    /// <summary>The largest results blob a node uploaded.</summary>
    public long LargestAnswerBlobBytes => Interlocked.Read(ref m_largestAnswerBytes);

    #endregion
}
