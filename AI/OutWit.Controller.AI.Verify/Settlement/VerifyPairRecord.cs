namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>Two executions of one task in one fan-out and the fields they disagree on.</summary>
/// <param name="Index">The task.</param>
/// <param name="Round">The fan-out the two ran in: the first one, or the repair round for a task the first left without a verdict.</param>
/// <param name="Primary">The first of the task's executions, in the order that follows from their content.</param>
/// <param name="Replica">Another execution of the task.</param>
/// <param name="Differs">The fields the two differ in; empty when they agree.</param>
internal sealed record VerifyPairRecord(int Index, string Round, VerifyExecution Primary, VerifyExecution Replica, List<string> Differs);
