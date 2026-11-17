using System.Collections;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// The tasks of a plan as a view of the manifest's sources: a task is made when it is asked for
/// and not kept, and every task points at the job's one pipeline and one list of origins, so a
/// plan of a hundred thousand shards holds nothing per shard beyond the manifest's own entry.
/// </summary>
internal sealed class CorpusPlanTasks : IReadOnlyList<CorpusTaskData>
{
    #region Fields

    private readonly IReadOnlyList<CorpusSourceData> m_sources;
    private readonly CorpusPipelineData m_pipeline;
    private readonly List<string> m_origins;
    private readonly long m_cap;

    #endregion

    #region Constructors

    /// <summary>Creates the view.</summary>
    /// <param name="sources">The manifest's sources, in order.</param>
    /// <param name="pipeline">The job's pipeline.</param>
    /// <param name="origins">The job's origins.</param>
    /// <param name="cap">The job's byte cap.</param>
    public CorpusPlanTasks(IReadOnlyList<CorpusSourceData> sources, CorpusPipelineData pipeline, List<string> origins, long cap)
    {
        m_sources = sources;
        m_pipeline = pipeline;
        m_origins = origins;
        m_cap = cap;
    }

    #endregion

    #region IReadOnlyList

    /// <summary>The task of a source: its index, its source, the job's pipeline, origins and cap, and the size the manifest declares for it.</summary>
    /// <param name="index">The source's index in the manifest.</param>
    public CorpusTaskData this[int index] => new()
    {
        TaskIndex = index,
        Source = m_sources[index],
        Pipeline = m_pipeline,
        AllowedOrigins = m_origins,
        MaxShardBytes = m_cap,
        DeclaredBytes = Math.Max(0, m_sources[index].SizeBytes)
    };

    /// <summary>The number of tasks.</summary>
    public int Count => m_sources.Count;

    /// <summary>The tasks in manifest order, each made as it is reached.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<CorpusTaskData> GetEnumerator()
    {
        for (var index = 0; index < m_sources.Count; index++)
            yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    #endregion
}
