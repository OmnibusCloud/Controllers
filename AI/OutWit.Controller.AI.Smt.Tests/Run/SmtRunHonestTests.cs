using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// SmtRun through the engine on three honest in-process nodes. The solver's golden problems,
/// each group of options a run, at a sampling rate of 1: every problem is answered on two
/// machines, the copies agree, and every row is the one the golden file names by its digest. At
/// 0, every problem rests on one machine; at the default rate, as many packages run twice as the
/// rate asks for. The documents are what their schemas say and name no machine; the bundled
/// scripts compile.
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunHonestTests : SmtRunTestBase
{
    #region Fields

    private readonly Dictionary<double, Task<SmtRunDocuments>> m_plain = new();

    #endregion

    #region Golden Tests

    [Test]
    public async Task EveryGoldenProblemIsAnsweredOnTwoMachinesAsTheGoldenRowsSayTest()
    {
        var golden = SmtRowGolden.Read().ToDictionary(row => row.Index);
        Assert.That(golden, Is.Not.Empty, $"the golden file {SmtRowGolden.FILE}");

        foreach (var (options, problems) in SmtGoldenPackages.Groups())
        {
            var taken = problems.Where(problem => problem.Id != "refused-hash").ToList();
            if (taken.Count == 0)
                continue;

            var run = await RunAsync(SmtSets.Golden(taken), Rate(options, 1));
            var (packages, copies, selected, complete, unanswered) = run.FanOut("main");

            Assert.Multiple(() =>
            {
                Assert.That(run.Rows, Has.Count.EqualTo(taken.Max(problem => problem.Index) + 1), "a row for every row of the set, refused ones too");
                Assert.That(copies, Is.EqualTo(2));
                Assert.That(selected, Is.EqualTo(packages), "every package was asked for twice");
                Assert.That(complete, Is.EqualTo(packages), "and came back twice");
                Assert.That(unanswered, Is.Zero);
                Assert.That(run.Report.GetProperty("run").GetProperty("sampling").GetString(), Is.EqualTo("as_asked"));

                foreach (var problem in taken)
                {
                    var row = run.Row(problem.Index);
                    Assert.That(row.GetProperty("id").GetString(), Is.EqualTo(problem.Id));
                    Assert.That(row.GetProperty("digest").GetString(), Is.EqualTo(golden[problem.Index].Digest), $"{problem.Id}: the digest of the golden file");
                    Assert.That(SmtRunChecks.Words(row), Is.EqualTo(SmtSolverCases.Of(problem.Id).Expected), problem.Id);
                    Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(2), $"{problem.Id}: two machines answered");
                    Assert.That(row.GetProperty("agreed").GetBoolean(), Is.True, problem.Id);
                    Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES), problem.Id);
                    var sat = row.GetProperty("verdict").GetString() == SmtResultWords.SAT && row.GetProperty("status").GetString() == SmtResultWords.OK;
                    Assert.That(row.GetProperty("validation").GetString(), sat ? Is.AnyOf(SmtRunWords.VALIDATED, SmtRunWords.PARTIAL) : Is.EqualTo(SmtRunWords.NOT_CHECKED),
                        $"{problem.Id}: every sat model is checked, and nothing else is");
                }

                foreach (var row in run.Rows.Where(row => row.GetProperty("id").GetString()!.StartsWith("other-", StringComparison.Ordinal)))
                    Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.REFUSED));

                Assert.That(run.IntegrityCount("disputes") + run.IntegrityCount("lies") + run.IntegrityCount("repairs") + run.IntegrityCount("rejected_results"), Is.Zero, "nothing to report");
                Assert.That(run.IntegrityCount("agreements"), Is.EqualTo(taken.Count));
                Assert.That(run.IntegrityCount("evaluator_solver_disagreements"), Is.Zero);
            });

            SmtRunChecks.AssertSchemas(run);
            SmtRunChecks.AssertNamesNoMachine(run);
        }
    }

    [Test]
    public async Task EverySatModelOfTheGoldenProblemsIsValidatedByTheHostTest()
    {
        var run = await PlainAsync(1);
        var sat = run.Rows.Where(row => row.GetProperty("status").GetString() == SmtResultWords.OK && row.GetProperty("verdict").GetString() == SmtResultWords.SAT).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sat, Has.Count.GreaterThan(15), "the golden problems answered sat");
            Assert.That(sat.Select(row => row.GetProperty("validation").GetString()), Has.All.EqualTo(SmtRunWords.VALIDATED),
                string.Join("; ", sat.Where(row => row.GetProperty("validation").GetString() != SmtRunWords.VALIDATED).Select(row => $"{row.GetProperty("id")}: {row.GetProperty("validation_reason")}")));
            Assert.That(run.Report.GetProperty("validation").GetProperty("classes").GetProperty("validated").GetInt32(), Is.EqualTo(sat.Count));
        });
    }

    #endregion

    #region Sampling Tests

    [Test]
    public async Task AtRateZeroEveryProblemRestsOnOneMachineTest()
    {
        var run = await PlainAsync(0);
        var answered = run.Rows.Where(row => row.GetProperty("status").GetString() != SmtRunWords.REFUSED).ToList();
        var (packages, _, selected, complete, unanswered) = run.FanOut("main");

        Assert.Multiple(() =>
        {
            Assert.That(answered.Select(row => row.GetProperty("copies").GetInt32()), Has.All.EqualTo(1));
            Assert.That(answered.Select(row => row.GetProperty("settled_by").GetString()), Has.All.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(packages, Is.GreaterThan(1));
            Assert.That(selected, Is.Zero, "no package was asked for twice");
            Assert.That(complete, Is.Zero, "and none came back twice");
            Assert.That(unanswered, Is.Zero, "every package came back");
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling").GetString(), Is.EqualTo("none"));
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling_rate").GetDouble(), Is.Zero, "the report states the rate");
            Assert.That(run.IntegrityCount("one_copy"), Is.EqualTo(answered.Count));
            Assert.That(run.IntegrityCount("compared"), Is.Zero);

            var unsat = answered.Where(row => row.GetProperty("verdict").GetString() == SmtResultWords.UNSAT).Select(row => row.GetProperty("index").GetInt32()).Order().ToList();
            Assert.That(unsat, Is.Not.Empty);
            Assert.That(run.Report.GetProperty("unsat_one_copy").GetProperty("indices").EnumerateArray().Select(index => index.GetInt32()), Is.EqualTo(unsat),
                "the report says which unsat rows were answered by one machine only");
            Assert.That(run.IntegrityCount("unsat_one_copy"), Is.EqualTo(unsat.Count));
        });
    }

    [Test]
    public async Task AtTheDefaultRateThePlannedShareRunsTwiceTest()
    {
        var run = await PlainAsync(SmtOptionsRules.DEFAULT_SAMPLING_RATE);
        var answered = run.Rows.Where(row => row.GetProperty("status").GetString() != SmtRunWords.REFUSED).ToList();
        var (packages, copies, selected, complete, unanswered) = run.FanOut("main");
        var twice = answered.Count(row => row.GetProperty("copies").GetInt32() == 2);

        TestContext.Out.WriteLine($"{answered.Count} problems in {packages} packages of at most {SmtRunRig.ProblemsPerPackage(answered.Count)}; {selected} package(s) asked for twice, {twice} problems answered twice");

        Assert.Multiple(() =>
        {
            Assert.That(packages, Is.EqualTo((answered.Count + SmtRunRig.ProblemsPerPackage(answered.Count) - 1) / SmtRunRig.ProblemsPerPackage(answered.Count)), "the planner's rule");
            Assert.That(copies, Is.EqualTo(2));
            Assert.That(selected, Is.EqualTo(SmtRunRig.Selected(packages, SmtOptionsRules.DEFAULT_SAMPLING_RATE)), "the share the rate asks for, by the rule Grid.Replicas selects by");
            Assert.That(complete, Is.EqualTo(selected), "the platform ran as many packages twice as the settlement expects of the rate");
            Assert.That(unanswered, Is.Zero);
            Assert.That(twice, Is.InRange(1, SmtRunRig.ProblemsPerPackage(answered.Count) * selected), "the problems of the packages that ran twice");
            Assert.That(run.IntegrityCount("compared"), Is.EqualTo(twice));
            Assert.That(run.IntegrityCount("agreements"), Is.EqualTo(twice));
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling").GetString(), Is.EqualTo("as_asked"));
            Assert.That(run.Report.GetProperty("run").GetProperty("sampling_rate").GetDouble(), Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE));
            Assert.That(run.Provenance.GetProperty("options").GetProperty("sampling_rate").GetDouble(), Is.EqualTo(SmtOptionsRules.DEFAULT_SAMPLING_RATE));
        });
    }

    #endregion

    #region Script Tests

    [TestCase("SmtRun.wit")]
    [TestCase("SmtPreflight.wit")]
    [TestCase("SmtRuntimeDiagnostics.wit")]
    public void TheBundledScriptCompilesTest(string script)
    {
        Assert.That(() => Engine.Compile(Script(script)), Throws.Nothing);
    }

    #endregion

    #region Tools

    /// <summary>A run of the golden problems of the plain options at a rate, made once for all the tests that read it.</summary>
    private Task<SmtRunDocuments> PlainAsync(double rate)
    {
        if (!m_plain.TryGetValue(rate, out var run))
        {
            var (options, problems) = SmtGoldenPackages.Groups().First(group => group.Options.Is(new SmtOptionsData()));
            m_plain[rate] = run = RunAsync(SmtSets.Golden(problems.Where(problem => problem.Id != "refused-hash").ToList()), Rate(options, rate));
        }

        return run;
    }

    private static SmtOptionsData Rate(SmtOptionsData options, double rate)
    {
        var copy = options.Clone();
        copy.SamplingRate = rate;
        return copy;
    }

    #endregion
}
