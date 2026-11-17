using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// The planner's answer: the tasks (empty when anything is wrong), the problems, what the
/// preflight reports — refused hosts, oversized sources, the bytes the contributors will fetch by
/// the manifest's own sizes — and notes worth a log line. Every list here is bounded whatever the
/// manifest holds; the tasks are not a list in memory but a view of the manifest's sources, each
/// task made when it is asked for, with the job's one pipeline and one list of origins.
/// </summary>
/// <param name="Tasks">One task per source, in manifest order; they share <see cref="Pipeline"/> and the origins, and carry no ticket yet.</param>
/// <param name="Errors">Problems that stop the plan: the first of them by line and, when there are more, one line saying how many.</param>
/// <param name="Notes">Informational notes.</param>
/// <param name="RefusedOrigins">Hosts named by sources that the allowlist does not cover (a bounded list).</param>
/// <param name="OutsideOrigins">Hosts that are neither public origins nor listed by the owner of the machine that planned the job: their shards run only on machines whose owner lists them (a bounded list).</param>
/// <param name="OversizedSources">Sources whose declared size exceeds the cap.</param>
/// <param name="EstimatedTrafficBytes">What the contributors will fetch by the manifest's own sizes: every source once and the sampled ones twice (the fetches of <see cref="CorpusTrafficEstimate"/>, before any size is asked at the origin).</param>
public sealed record CorpusPlan(
    IReadOnlyList<CorpusTaskData> Tasks,
    List<string> Errors,
    List<string> Notes,
    List<string> RefusedOrigins,
    List<string> OutsideOrigins,
    int OversizedSources,
    long EstimatedTrafficBytes)
{
    #region Properties

    /// <summary>How many problems there are in all; <see cref="Errors"/> lists a bounded number of them.</summary>
    public int ErrorCount { get; init; } = Errors.Count;

    /// <summary>The job's pipeline, completed with the language model's transport and pin: one object for every task.</summary>
    public CorpusPipelineData Pipeline { get; init; } = new();

    /// <summary>The byte cap of the job.</summary>
    public long MaxShardBytes { get; init; }

    #endregion
}
