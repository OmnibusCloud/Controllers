using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>One problem of the benchmark's reference set (<see cref="SmtBenchmarkProblems"/>).</summary>
/// <param name="Id">The problem's name in the set.</param>
/// <param name="Problem">The problem as it travels.</param>
/// <param name="Expected">Its outcome in words (<see cref="SmtBenchmark.Words"/>).</param>
/// <param name="Fuel">The fuel its session costs on every machine.</param>
internal sealed record SmtBenchmarkProblem(string Id, SmtProblemData Problem, string Expected, long Fuel);
