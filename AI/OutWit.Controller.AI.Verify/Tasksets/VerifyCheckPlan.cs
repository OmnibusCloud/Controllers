using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>The check tasks and the candidates that produced none.</summary>
/// <param name="Tasks">Check tasks ordered by index.</param>
/// <param name="Skipped">One note per candidate without a check task.</param>
public sealed record VerifyCheckPlan(List<VerifyTaskData> Tasks, List<string> Skipped);
