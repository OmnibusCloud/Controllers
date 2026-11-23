using System.Globalization;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// The node benchmark: its reference set solved, each problem to the outcome and the fuel written
/// beside it (the same on every machine, so a node that computes otherwise cannot score), and the
/// set timed at a degree into a rate of problems a second that depends on the machine's speed and
/// nothing else of it.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtBenchmarkTests
{
    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        SmtSolverModule.Require();
        SmtSolverRig.Warm();
    }

    #endregion

    #region Set Tests

    [Test]
    public void EveryProblemOfTheSetComesToItsOutcomeAndItsFuelTest()
    {
        var set = SmtDiagnosticsRig.BenchmarkSet();
        long fuel = 0;
        long wall = 0;

        Assert.Multiple(() =>
        {
            Assert.That(set, Has.Count.EqualTo(30));
            Assert.That(set.Select(problem => problem.Id), Is.Unique);
            Assert.That(set.Select(problem => problem.Problem.Index), Is.EqualTo(Enumerable.Range(0, set.Count)));
            Assert.That(set.Select(problem => problem.Problem.Query).Distinct().Count(), Is.EqualTo(3), "every query is in the set");

            foreach (var (id, problem, expected, pinned) in set)
            {
                var (words, used, wallMs) = SmtDiagnosticsRig.BenchmarkWords(problem);
                fuel += used;
                wall += wallMs;
                TestContext.Out.WriteLine($"{id,-18} {words,-24} fuel {used.ToString(CultureInfo.InvariantCulture),12} {wallMs,5} ms");

                Assert.That(words, Is.EqualTo(expected), id);
                Assert.That(used, Is.EqualTo(pinned), $"{id}: the fuel, the same on every machine");
            }
        });

        TestContext.Out.WriteLine($"the set: fuel {fuel:N0}, {wall} ms one by one on this machine");
    }

    #endregion

    #region Rate Tests

    [Test]
    public void TheSetIsTimedAtADegreeIntoARateOfProblemsASecondTest()
    {
        const int DEGREE = 2;
        var result = SmtDiagnosticsRig.Benchmark(DEGREE, new WitBenchmarkOptions { MinDuration = TimeSpan.FromMilliseconds(200) });

        TestContext.Out.WriteLine($"{result.Rate:F2} {result.Unit}; {string.Join(", ", result.Custom!.Select(pair => $"{pair.Key}={pair.Value}"))}");
        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(SmtDiagnosticsRig.BenchmarkUnit));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Iterations, Is.InRange(3, 5));
            Assert.That(result.Custom!["degree"], Is.EqualTo("2"));
            Assert.That(result.Custom["problems"], Is.EqualTo("30"), "the set once: two sessions get four problems each and more");
            Assert.That(result.Custom.Keys, Is.SupersetOf(new[] { "median_s", "runs_s", "warmup_s", "platform", "cores", "rounds" }));
            Assert.That(string.Join(";", result.Custom.Values), Does.Not.Contain(Environment.MachineName), "the result names no machine");
        });
    }

    [Test]
    public void EachSessionGetsAboutFourProblemsARunTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtDiagnosticsRig.BenchmarkRounds(1), Is.EqualTo(1));
            Assert.That(SmtDiagnosticsRig.BenchmarkRounds(7), Is.EqualTo(1));
            Assert.That(SmtDiagnosticsRig.BenchmarkRounds(8), Is.EqualTo(2), "32 problems for 8 sessions: the set twice");
            Assert.That(SmtDiagnosticsRig.BenchmarkRounds(16), Is.EqualTo(3));
        });
    }

    [Test]
    public void ThePoolAddsUpTheMeasuredNodesWithoutNamingOneTest()
    {
        var measured = new WitBenchmarkResult { Rate = 10, Unit = SmtDiagnosticsRig.BenchmarkUnit, Custom = new Dictionary<string, string> { ["degree"] = "4" } };
        var other = new WitBenchmarkResult { Rate = 7, Unit = SmtDiagnosticsRig.BenchmarkUnit, Custom = new Dictionary<string, string> { ["degree"] = "2" } };
        var stale = new WitBenchmarkResult { Rate = 99, Unit = "verify-task@mixed-v2" };

        Assert.Multiple(() =>
        {
            Assert.That(SmtDiagnosticsRig.Pool([measured, other, stale, WitBenchmarkResult.Default]), Is.EqualTo((4, 2, (int?)6, 17.0)));
            Assert.That(SmtDiagnosticsRig.Pool([measured, new WitBenchmarkResult { Rate = 3, Unit = SmtDiagnosticsRig.BenchmarkUnit }]), Is.EqualTo((2, 2, (int?)null, 13.0)), "a node without its degree leaves the total out");
        });
    }

    #endregion
}
