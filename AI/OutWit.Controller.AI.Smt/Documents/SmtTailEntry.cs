namespace OutWit.Controller.AI.Smt.Documents;

/// <summary>One problem of the long tail: what its settled row cost, and what stopped it when a budget did.</summary>
/// <param name="Index">The problem.</param>
/// <param name="ResourceCount">The solver's resource counter of the row.</param>
/// <param name="Fuel">The fuel of the row.</param>
/// <param name="StoppedBy">The reason of an unknown that a budget gave (<c>rlimit</c>, <c>fuel</c>, <c>memout</c>), else empty.</param>
internal readonly record struct SmtTailEntry(int Index, long ResourceCount, long Fuel, string StoppedBy);
