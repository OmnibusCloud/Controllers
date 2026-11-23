using OutWit.Controller.AI.Smt.Diagnostics;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests meet what the controller says of itself and of the node: the diagnostics, the
/// capabilities document, the benchmark's reference set and rate, the pool's totals. As in
/// <see cref="SmtNodeRig"/>, the controller's types are met inside method bodies only.
/// </summary>
internal static class SmtDiagnosticsRig
{
    #region Diagnostics

    /// <summary>The diagnostics of this process's controller.</summary>
    /// <returns>The diagnostics.</returns>
    public static SmtRuntimeDiagnosticsData Diagnostics()
    {
        return SmtRuntimeDiagnosticsBuilder.Build();
    }

    /// <summary>The capabilities document for diagnostics and a pool.</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <param name="nodes">The pool's nodes, or -1 for a host that cannot say.</param>
    /// <param name="measured">Of them, the measured ones.</param>
    /// <param name="workers">Their sessions at once, or null.</param>
    /// <param name="rate">Their rate.</param>
    /// <returns>The document's bytes.</returns>
    public static byte[] Capabilities(SmtRuntimeDiagnosticsData diagnostics, int nodes = -1, int measured = 0, int? workers = null, double rate = 0)
    {
        return SmtCapabilitiesWriter.Write(diagnostics, nodes < 0 ? null : new SmtPoolSummary(nodes, measured, workers, rate));
    }

    /// <summary>The capabilities document's schema tag.</summary>
    public static string CapabilitiesSchema => SmtCapabilitiesWriter.SCHEMA;

    #endregion

    #region Benchmark

    /// <summary>The benchmark's reference set.</summary>
    /// <returns>Each problem with the words and the fuel it must come to.</returns>
    public static IReadOnlyList<(string Id, SmtProblemData Problem, string Expected, long Fuel)> BenchmarkSet()
    {
        var set = new List<(string, SmtProblemData, string, long)>();
        foreach (var problem in SmtBenchmarkProblems.All)
            set.Add((problem.Id, problem.Problem, problem.Expected, problem.Fuel));

        return set;
    }

    /// <summary>A problem solved under the default options in the process's sandbox, in the benchmark's words.</summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The words and the fuel.</returns>
    public static (string Words, long Fuel, int WallMs) BenchmarkWords(SmtProblemData problem)
    {
        var outcome = (SmtSolveOutcome)SmtSolverRig.Raw(problem, new SmtOptionsData(), null);
        return (SmtBenchmark.Words(outcome), outcome.Fuel, outcome.WallMs);
    }

    /// <summary>Measures the process's sandbox at a degree.</summary>
    /// <param name="degree">The sessions at once.</param>
    /// <param name="options">The engine's benchmark options.</param>
    /// <returns>The result.</returns>
    public static IWitBenchmarkResult Benchmark(int degree, IWitBenchmarkOptions? options)
    {
        var sandbox = SmtSandboxHost.Get(out var unavailable) ?? throw new InvalidOperationException(unavailable);
        return SmtBenchmark.Measure(sandbox, degree, options, CancellationToken.None);
    }

    /// <summary>The benchmark's unit.</summary>
    public static string BenchmarkUnit => SmtBenchmark.UNIT;

    /// <summary>How many times one run of the benchmark gives the set at a degree.</summary>
    /// <param name="degree">The degree.</param>
    /// <returns>The rounds.</returns>
    public static int BenchmarkRounds(int degree)
    {
        return SmtBenchmark.Rounds(degree);
    }

    /// <summary>The pool's totals of benchmark results.</summary>
    /// <param name="benchmarks">One result per node.</param>
    /// <returns>Nodes, measured nodes, their sessions at once (or null), their rate.</returns>
    public static (int Nodes, int Measured, int? Workers, double Rate) Pool(IEnumerable<IWitBenchmarkResult> benchmarks)
    {
        var summary = SmtPoolProbe.Summarize(benchmarks);
        return (summary.Nodes, summary.Measured, summary.Workers, summary.Rate);
    }

    #endregion
}
