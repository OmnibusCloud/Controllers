using System.Collections;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// The rig's three in-process nodes with a meter on the door: everything a share brings to a
/// node and everything a node returns is measured as it passes, in the bytes it has on the
/// wire (the MemoryPack image of every Verify value in a request's variables and in the
/// variables a node returns). What a message carries is then a number, not a belief.
/// </summary>
internal sealed class VerifyMeasuringNodesManager : IWitNodesManager
{
    #region Fields

    private readonly VerifyTestNodesManager m_inner;
    private readonly object m_lock = new();
    private readonly SortedSet<string> m_otherTypes = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    public VerifyMeasuringNodesManager(IWitEngineNode node)
    {
        m_inner = new VerifyTestNodesManager(node);
    }

    #endregion

    #region IWitNodesManager

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options)
        where TActivity : IWitActivity
    {
        return m_inner.GetCompatibleNodes<TActivity>(options);
    }

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
    {
        return m_inner.GetCompatibleNodes(activityType, options);
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity, IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
    {
        Sent(1, Measure(pool));
        var answer = await m_inner.Process(nodeId, jobId, activity, pool, returnVariables);
        Returned(Measure(answer.Item2));
        return answer;
    }

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId, IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
    {
        long share = 0;
        long largest = 0;
        foreach (var request in requests)
        {
            var bytes = Measure(request.Pool);
            share += bytes;
            largest = Math.Max(largest, bytes);
        }

        Sent(requests.Count, share, largest);
        FirstShareAt ??= DateTime.UtcNow;

        var answer = await m_inner.ProcessBatch(nodeId, jobId, requests, canRunInParallelOnClient);
        Returned(Measure(answer.Item2));
        return answer;
    }

    #endregion

    #region Tools

    private void Sent(int requests, long bytes, long largestRequest = -1)
    {
        lock (m_lock)
        {
            Shares++;
            Requests += requests;
            SentBytes += bytes;
            LargestShareBytes = Math.Max(LargestShareBytes, bytes);
            LargestShareRequests = Math.Max(LargestShareRequests, requests);
            LargestRequestBytes = Math.Max(LargestRequestBytes, largestRequest < 0 ? bytes : largestRequest);
        }
    }

    private void Returned(long bytes)
    {
        lock (m_lock)
        {
            ReturnedBytes += bytes;
            LargestReturnBytes = Math.Max(LargestReturnBytes, bytes);
        }
    }

    private long Measure(IEnumerable<IWitVariable> variables)
    {
        long bytes = 0;
        foreach (var variable in variables)
            bytes += Measure(variable.Value);

        return bytes;
    }

    private long Measure(object? value)
    {
        switch (value)
        {
            case null:
                return 0;
            case VerifyTaskPackageData package:
                return MemoryPackSerializer.Serialize(package).Length;
            case VerifyResultPackageData result:
                return MemoryPackSerializer.Serialize(result).Length;
            case VerifyOptionsData options:
                return MemoryPackSerializer.Serialize(options).Length;
            case string text:
                return text.Length;
            case byte[] raw:
                return raw.Length;
            case Guid:
                return 16;
            case IEnumerable items:
                long bytes = 0;
                foreach (var item in items)
                    bytes += Measure(item);
                return bytes;
            default:
                // A value of the platform's own (a number, a flag, its options): counted as a
                // word, and its type named so that the account says what it did not weigh.
                lock (m_lock)
                    m_otherTypes.Add(value.GetType().Name);
                return 8;
        }
    }

    #endregion

    #region Properties

    /// <summary>Shares given to a node.</summary>
    public int Shares { get; private set; }

    /// <summary>Requests in all the shares together: one a package a node was given.</summary>
    public int Requests { get; private set; }

    /// <summary>The bytes of every value the shares carried to nodes.</summary>
    public long SentBytes { get; private set; }

    /// <summary>The bytes of the largest share: what one message to one node carries.</summary>
    public long LargestShareBytes { get; private set; }

    /// <summary>The most requests one share held.</summary>
    public int LargestShareRequests { get; private set; }

    /// <summary>The bytes of the largest single request: one activity's parameters.</summary>
    public long LargestRequestBytes { get; private set; }

    /// <summary>The bytes of every value nodes returned.</summary>
    public long ReturnedBytes { get; private set; }

    /// <summary>The bytes of the largest return of one share.</summary>
    public long LargestReturnBytes { get; private set; }

    /// <summary>When the first share went out: the end of the split.</summary>
    public DateTime? FirstShareAt { get; private set; }

    /// <summary>The types of the values that were counted as a word each.</summary>
    public IReadOnlyCollection<string> OtherTypes => m_otherTypes;

    #endregion
}
