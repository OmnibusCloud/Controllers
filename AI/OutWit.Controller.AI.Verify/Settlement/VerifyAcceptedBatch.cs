using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>One batch of an accepted result package.</summary>
/// <param name="Diagnostics">What the node said of the batch: platform, engine, controller, runtime.</param>
/// <param name="Executions">The batch's executions, one a task.</param>
internal sealed record VerifyAcceptedBatch(VerifyBatchDiagnosticsData Diagnostics, List<VerifyExecution> Executions);
