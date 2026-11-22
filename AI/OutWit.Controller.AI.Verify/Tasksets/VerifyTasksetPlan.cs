using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>What planning a taskset came to.</summary>
/// <param name="Batches">The batches, in plan order.</param>
/// <param name="Notes">One note per limit that was clamped.</param>
/// <param name="Errors">One message per task that cannot be planned, by its index; a taskset with any is refused.</param>
public sealed record VerifyTasksetPlan(List<VerifyTaskBatchData> Batches, List<string> Notes, List<string> Errors);
