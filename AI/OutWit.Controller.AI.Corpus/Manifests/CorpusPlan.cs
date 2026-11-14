using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Manifests;

/// <summary>
/// The planner's answer: the tasks (empty when anything is wrong), the problems, what the
/// preflight reports — refused hosts, oversized sources, the traffic the contributors will
/// carry — and notes worth a log line.
/// </summary>
/// <param name="Tasks">One task per source, in manifest order.</param>
/// <param name="Errors">Problems that stop the plan.</param>
/// <param name="Notes">Informational notes.</param>
/// <param name="RefusedOrigins">Hosts named by sources that the allowlist does not cover.</param>
/// <param name="OutsideOrigins">Hosts that are neither public origins nor listed by the owner of the machine that planned the job: their shards run only on machines whose owner lists them.</param>
/// <param name="OversizedSources">Sources whose estimated size exceeds the cap.</param>
/// <param name="EstimatedTrafficBytes">What the contributors will fetch by the manifest's sizes: every source once and the sampled ones twice (<see cref="CorpusTrafficEstimate"/>).</param>
public sealed record CorpusPlan(
    List<CorpusTaskData> Tasks,
    List<string> Errors,
    List<string> Notes,
    List<string> RefusedOrigins,
    List<string> OutsideOrigins,
    int OversizedSources,
    long EstimatedTrafficBytes);
