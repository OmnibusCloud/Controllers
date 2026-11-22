using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>Parse outcome: the tasks that parsed and the malformed lines.</summary>
/// <param name="Tasks">The tasks that parsed, in the taskset's order.</param>
/// <param name="Errors">One message per malformed line, bounded in number and length; when there are more, the last says how many.</param>
/// <param name="ErrorCount">How many lines are malformed, shown or not.</param>
public sealed record VerifyTasksetParseResult(List<VerifyTaskData> Tasks, List<string> Errors, int ErrorCount);
