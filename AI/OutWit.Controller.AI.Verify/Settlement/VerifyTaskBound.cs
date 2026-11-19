namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>What a result of one task may say at the most: the task's own limits, as the server holds them.</summary>
/// <param name="Fuel">The most fuel the task's runs can use together: its budget, once a case.</param>
/// <param name="MemoryBytes">The task's memory cap.</param>
/// <param name="OutputCharacters">The most characters of stdout the task may keep, all its cases together.</param>
/// <param name="Cases">The cases of the task's suite; 0 for a task without one.</param>
internal readonly record struct VerifyTaskBound(long Fuel, long MemoryBytes, int OutputCharacters, int Cases);
