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
/// <param name="OversizedSources">Sources whose estimated size exceeds the cap.</param>
/// <param name="EstimatedTrafficBytes">Sum of the sources' estimated sizes.</param>
public sealed record CorpusPlan(
    List<CorpusTaskData> Tasks,
    List<string> Errors,
    List<string> Notes,
    List<string> RefusedOrigins,
    int OversizedSources,
    long EstimatedTrafficBytes);
