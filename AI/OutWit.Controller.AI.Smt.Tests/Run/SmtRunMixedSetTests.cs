using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Run;

/// <summary>
/// One problem set that mixes every query and every status a run gives a row, through SmtRun on
/// the in-process nodes, and every field of the four documents held to it: refused rows of three
/// kinds, a check answered sat with the values it asked for (and an attribution), one answered
/// unsat against the verdict its row expects, an error of the solver's, an unknown by fuel and one
/// by memory, enumerations with exactly one model and with four, one that writes past the output
/// cap, a proof and a counterexample, and a problem no node answers even after the repair round
/// (every node's session of it ends at the wall-clock backstop: a fault of the node, made by the
/// rig, since no honest session of a test can be made to end so).
/// </summary>
[TestFixture]
[Category("Solver")]
[NonParallelizable]
public sealed class SmtRunMixedSetTests : SmtRunTestBase
{
    #region Constants

    private const string SOURCE = "Written for the tests of the Smt controller";
    private const string LICENSE = "https://creativecommons.org/licenses/by/4.0/";
    private const int OUTPUT_CONSTANTS = 400;
    private const long FUEL_PER_SECOND = 5_000_000_000;
    private const int HARD_CAP_SECONDS = 5;

    private static readonly SmtOptionsData OPTIONS = new()
    {
        ResourceLimit = SmtOptionsRules.MAX_RESOURCE_LIMIT,
        HardCapSeconds = HARD_CAP_SECONDS,
        MemoryMegabytes = 64,
        SamplingRate = 0
    };

    #endregion

    #region Fields

    private string m_set = null!;
    private SmtRunDocuments m_run = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_set = SmtSets.Of(Lines());

        // Every node ends the session of one problem at its backstop, in every round.
        Liars.Liars = SmtLiars.All;
        Liars.Lie = honest => Task.FromResult<SmtResultPackageData?>(SmtRunRig.Forge(Blobs, honest, rows =>
        {
            foreach (var row in rows.Where(row => row.Id == "lost"))
                SmtRunRig.Backstop(row);
        }));

        try
        {
            m_run = await RunAsync(m_set, OPTIONS);
        }
        finally
        {
            Liars.Lie = null;
        }

        TestContext.Out.WriteLine(m_run.ReportText.Length > 4000 ? m_run.ReportText[..4000] : m_run.ReportText);
        TestContext.Out.WriteLine(m_run.IntegrityText);
    }

    #endregion

    #region Results Tests

    [Test]
    public void EveryRowOfTheSetHasItsRowInItsPlaceTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_run.Rows.Select(row => row.GetProperty("index").GetInt32()), Is.EqualTo(Enumerable.Range(0, 14)), "one row a row of the set, in its order");
            Assert.That(m_run.Rows.Select(row => row.GetProperty("id").GetString()),
                Is.EqualTo(new[] { "sat-values", "unsat-expected-sat", "no-text", "undeclared", "fuel", "memory", "one-model", "four-models", "past-the-cap", "proved", "counterexample", "set-option", "", "lost" }), "a row refused for its id has none");
        });

        SmtRunChecks.AssertSchemas(m_run);
        SmtRunChecks.AssertNamesNoMachine(m_run);
    }

    [Test]
    public void ACheckAnsweredSatSaysEveryFieldTest()
    {
        var row = m_run.Row(0);

        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("query").GetString(), Is.EqualTo("check"));
            Assert.That(row.GetProperty("logic").GetString(), Is.EqualTo("QF_LIA"));
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(row.GetProperty("code").GetString(), Is.Empty);
            Assert.That(row.GetProperty("message").GetString(), Is.Empty);
            Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("sat"));
            Assert.That(row.GetProperty("reason").GetString(), Is.EqualTo("none"));
            Assert.That(row.GetProperty("expected").GetString(), Is.EqualTo("sat"), "the problem's :status");
            Assert.That(row.GetProperty("expected_agreement").GetString(), Is.EqualTo(SmtRunWords.AGREE));
            Assert.That(row.GetProperty("model").GetString(), Does.Contain("define-fun x"));
            Assert.That(row.GetProperty("values").GetString(), Does.Contain("(+ x y)"), "the values asked");
            Assert.That(row.GetProperty("model_truncated").GetBoolean(), Is.False);
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(row.GetProperty("validation_reason").GetString(), Is.Empty);
            Assert.That(row.TryGetProperty("validation_detail", out _), Is.False);
            Assert.That(row.GetProperty("copies").GetInt32(), Is.EqualTo(1));
            Assert.That(row.GetProperty("agreed").GetBoolean(), Is.True);
            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(row.GetProperty("digest").GetString(), Does.Match("^[0-9a-f]{64}$"));
            Assert.That(row.GetProperty("stats").GetProperty("checks").GetInt32(), Is.EqualTo(1));
            Assert.That(row.GetProperty("stats").GetProperty("resource_count").GetInt64(), Is.GreaterThan(0));
            Assert.That(row.GetProperty("stats").GetProperty("fuel").GetInt64(), Is.GreaterThan(0));
            Assert.That(row.GetProperty("tags").GetString(), Is.EqualTo("tagged"));
        });
    }

    [Test]
    public void TheOtherVerdictsAndStatusesSayTheirFieldsTest()
    {
        Assert.Multiple(() =>
        {
            var unsat = m_run.Row(1);
            Assert.That(SmtRunChecks.Words(unsat), Is.EqualTo("ok unsat"));
            Assert.That(unsat.GetProperty("expected").GetString(), Is.EqualTo("sat"), "the row's own expected verdict");
            Assert.That(unsat.GetProperty("expected_agreement").GetString(), Is.EqualTo(SmtRunWords.DISAGREE));
            Assert.That(unsat.GetProperty("model").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(unsat.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.NOT_CHECKED));

            var error = m_run.Row(3);
            Assert.That(error.GetProperty("status").GetString(), Is.EqualTo("input_error"));
            Assert.That(error.GetProperty("message").GetString(), Is.Not.Empty, "the solver's message");
            Assert.That(error.GetProperty("verdict").GetString(), Is.EqualTo("none"));
            Assert.That(error.GetProperty("expected_agreement").GetString(), Is.EqualTo(SmtRunWords.NOT_APPLICABLE));

            var fuel = m_run.Row(4);
            Assert.That(SmtRunChecks.Words(fuel), Is.EqualTo("ok unknown fuel"));
            Assert.That(fuel.GetProperty("stats").GetProperty("fuel").GetInt64(), Is.EqualTo(HARD_CAP_SECONDS * FUEL_PER_SECOND), "the hard cap's fuel, to the unit");

            Assert.That(SmtRunChecks.Words(m_run.Row(5)), Is.EqualTo("ok unknown memout"));

            var one = m_run.Row(6);
            Assert.That(SmtRunChecks.Words(one), Is.EqualTo("ok sat 1 complete"));
            Assert.That(one.GetProperty("models").GetArrayLength(), Is.EqualTo(1));
            Assert.That(one.GetProperty("models")[0].GetProperty("values").GetString(), Does.Contain("x"));
            Assert.That(one.GetProperty("models")[0].GetProperty("model").GetString(), Does.Contain("define-fun"));
            Assert.That(one.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "the model of an enumeration is checked");

            var four = m_run.Row(7);
            Assert.That(SmtRunChecks.Words(four), Is.EqualTo("ok sat 4 complete"));
            Assert.That(four.GetProperty("models").GetArrayLength(), Is.EqualTo(4));
            Assert.That(four.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "each of them");

            var past = m_run.Row(8);
            Assert.That(past.GetProperty("status").GetString(), Is.EqualTo("output_exceeded"));
            Assert.That(past.GetProperty("verdict").GetString(), Is.EqualTo("sat"), "the verdict stands");
            Assert.That(past.GetProperty("count").GetInt32(), Is.GreaterThan(100));
            Assert.That(past.GetProperty("complete").GetBoolean(), Is.False);
            Assert.That(past.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.PARTIAL), "its models are not checked");
            Assert.That(past.GetProperty("validation_reason").GetString(), Is.EqualTo(SmtRunWords.OUTPUT_EXCEEDED));
            Assert.That(past.GetProperty("model_truncated").GetBoolean(), Is.True, "a lab sees 64 KB of a row's models");
            Assert.That(Encoding.UTF8.GetByteCount(past.GetRawText()), Is.LessThan(SmtRunRig.MaxModelBytes + 64 * 1024));

            var proved = m_run.Row(9);
            Assert.That(SmtRunChecks.Words(proved), Is.EqualTo("ok unsat proved"));
            Assert.That(proved.GetProperty("proof").GetString(), Is.EqualTo("proved"));
            Assert.That(proved.GetProperty("expected").GetString(), Is.EqualTo("none"), "a proof expects nothing");

            var counter = m_run.Row(10);
            Assert.That(SmtRunChecks.Words(counter), Is.EqualTo("ok sat counterexample"));
            Assert.That(counter.GetProperty("model").GetString(), Does.Contain("define-fun x"));
            Assert.That(counter.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "against the problem and the negated goal");

            var lost = m_run.Row(13);
            Assert.That(lost.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.NO_RESULT));
            Assert.That(lost.GetProperty("code").GetString(), Is.EqualTo("backstop"), "what the nodes said last");
            Assert.That(lost.GetProperty("verdict").GetString(), Is.EqualTo("none"));
            Assert.That(lost.GetProperty("copies").GetInt32(), Is.Zero);
            Assert.That(lost.GetProperty("settled_by").GetString(), Is.Empty);
            Assert.That(lost.TryGetProperty("stats", out _), Is.False);
        });
    }

    [Test]
    public void RefusedRowsSayTheirCodeAndNothingElseTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (index, code) in new[] { (2, "field_missing"), (11, "command_not_allowed"), (12, "id_invalid") })
            {
                var row = m_run.Row(index);
                Assert.That(row.GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.REFUSED), $"row {index}");
                Assert.That(row.GetProperty("code").GetString(), Is.EqualTo(code), $"row {index}");
                Assert.That(row.GetProperty("message").GetString(), Is.Not.Empty, $"row {index}");
                Assert.That(row.TryGetProperty("query", out _), Is.False, $"row {index}: no problem was taken");
                Assert.That(row.GetProperty("copies").GetInt32(), Is.Zero, $"row {index}");
                Assert.That(row.GetProperty("verdict").GetString(), Is.EqualTo("none"), $"row {index}");
            }
        });
    }

    #endregion

    #region Report Tests

    [Test]
    public void TheReportCountsTheSetAsItWasSettledTest()
    {
        var report = m_run.Report;

        Assert.Multiple(() =>
        {
            Assert.That(report.GetProperty("schema").GetString(), Is.EqualTo(SmtRunRig.Schemas.Report));

            var run = report.GetProperty("run");
            Assert.That(run.GetProperty("profile").GetString(), Is.EqualTo("z3-5.4.1"));
            Assert.That(run.GetProperty("module_sha256").GetString(), Is.EqualTo(SmtSolverModule.SHA256));
            Assert.That(run.GetProperty("controller").GetString(), Is.EqualTo(SmtNodeRig.ControllerVersion));
            Assert.That(run.GetProperty("sampling_rate").GetDouble(), Is.Zero);
            Assert.That(run.GetProperty("sampling").GetString(), Is.EqualTo("none"));
            Assert.That(run.GetProperty("options").GetProperty("rlimit").GetInt64(), Is.EqualTo(SmtOptionsRules.MAX_RESOURCE_LIMIT), "the number, never the word default");
            Assert.That(run.GetProperty("options").GetProperty("fuel").GetInt64(), Is.EqualTo(HARD_CAP_SECONDS * FUEL_PER_SECOND));

            var set = report.GetProperty("problem_set");
            Assert.That(set.GetProperty("rows").GetInt32(), Is.EqualTo(14));
            Assert.That(set.GetProperty("accepted").GetInt32(), Is.EqualTo(11));
            Assert.That(set.GetProperty("refused").GetInt32(), Is.EqualTo(3));
            Assert.That(set.GetProperty("refused_by_code").GetProperty("command_not_allowed").GetInt32(), Is.EqualTo(1));
            Assert.That(set.GetProperty("logics").GetProperty("QF_LIA").GetInt32(), Is.GreaterThan(1));

            Assert.That(m_run.ReportCount("statuses", "ok"), Is.EqualTo(8));
            Assert.That(m_run.ReportCount("statuses", "refused"), Is.EqualTo(3));
            Assert.That(m_run.ReportCount("statuses", "input_error"), Is.EqualTo(1));
            Assert.That(m_run.ReportCount("statuses", "output_exceeded"), Is.EqualTo(1));
            Assert.That(m_run.ReportCount("statuses", "no_result"), Is.EqualTo(1));
            Assert.That(m_run.ReportCount("verdicts", "sat"), Is.EqualTo(5));
            Assert.That(m_run.ReportCount("verdicts", "unsat"), Is.EqualTo(2));
            Assert.That(m_run.ReportCount("verdicts", "unknown"), Is.EqualTo(2));
            Assert.That(m_run.ReportCount("unknown", "fuel"), Is.EqualTo(1));
            Assert.That(m_run.ReportCount("unknown", "memout"), Is.EqualTo(1));
            Assert.That(m_run.ReportCount("expected", "agree"), Is.EqualTo(1));
            Assert.That(m_run.ReportCount("expected", "disagree"), Is.EqualTo(1));

            var classes = report.GetProperty("validation").GetProperty("classes");
            Assert.That(classes.GetProperty("validated").GetInt32(), Is.EqualTo(4));
            Assert.That(classes.GetProperty("partial").GetInt32(), Is.EqualTo(1));
            Assert.That(classes.GetProperty("failed").GetInt32(), Is.Zero);
            Assert.That(report.GetProperty("validation").GetProperty("partial_by_reason").GetProperty(SmtRunWords.OUTPUT_EXCEEDED).GetInt32(), Is.EqualTo(1));
            Assert.That(report.GetProperty("validation").GetProperty("by_logic").GetProperty("QF_BV").GetProperty("validated").GetInt32(), Is.EqualTo(1));

            Assert.That(report.GetProperty("unsat_one_copy").GetProperty("indices").EnumerateArray().Select(index => index.GetInt32()), Is.EqualTo(new[] { 1, 9 }));

            var tail = report.GetProperty("long_tail");
            Assert.That(tail.GetProperty("rlimit").GetInt64(), Is.EqualTo(SmtOptionsRules.MAX_RESOURCE_LIMIT));
            Assert.That(tail.GetProperty("fuel").GetInt64(), Is.EqualTo(HARD_CAP_SECONDS * FUEL_PER_SECOND));
            Assert.That(tail.GetProperty("problems").GetArrayLength(), Is.EqualTo(10), "every problem with a settled row");
            Assert.That(tail.GetProperty("problems")[0].GetProperty("index").GetInt32(), Is.EqualTo(4), "the most fuel first");
            Assert.That(tail.GetProperty("problems")[0].GetProperty("stopped_by").GetString(), Is.EqualTo("fuel"));
            var fuels = tail.GetProperty("problems").EnumerateArray().Select(entry => entry.GetProperty("fuel").GetInt64()).ToList();
            Assert.That(fuels, Is.Ordered.Descending);

            var sources = report.GetProperty("attribution").GetProperty("sources");
            Assert.That(sources.GetArrayLength(), Is.EqualTo(1));
            Assert.That(sources[0].GetProperty("source").GetString(), Is.EqualTo(SOURCE));
            Assert.That(sources[0].GetProperty("license").GetString(), Is.EqualTo(LICENSE));
            Assert.That(sources[0].GetProperty("problems").GetInt32(), Is.EqualTo(1));

            var enumerate = report.GetProperty("enumerate");
            Assert.That(enumerate.GetProperty("kept").EnumerateArray().Select(index => index.GetInt32()), Is.EqualTo(new[] { 6 }), "exactly one model");
            Assert.That(enumerate.GetProperty("dropped").EnumerateArray().Select(index => index.GetInt32()), Is.EqualTo(new[] { 7 }), "two or more");
        });
    }

    [Test]
    public void TheIntegrityAndTheProvenanceSayWhatTheRunDidAndRanWithTest()
    {
        var provenance = m_run.Provenance;

        Assert.Multiple(() =>
        {
            Assert.That(m_run.IntegrityCount("problems"), Is.EqualTo(11));
            Assert.That(m_run.IntegrityCount("one_copy"), Is.EqualTo(10));
            Assert.That(m_run.IntegrityCount("unsat_one_copy"), Is.EqualTo(2));
            Assert.That(m_run.IntegrityCount("repairs"), Is.EqualTo(1));
            Assert.That(m_run.IntegrityCount("repairs_by_reason", "backstop"), Is.EqualTo(1));
            Assert.That(m_run.IntegrityCount("no_result"), Is.EqualTo(1));
            Assert.That(m_run.IntegrityCount("no_result_by_reason", "backstop"), Is.EqualTo(1));
            Assert.That(m_run.IntegrityCount("disputes") + m_run.IntegrityCount("lies") + m_run.IntegrityCount("rejected_results"), Is.Zero);
            Assert.That(m_run.FanOut("repair").Packages, Is.EqualTo(1), "the problem went out once more");

            Assert.That(provenance.GetProperty("schema").GetString(), Is.EqualTo(SmtRunRig.Schemas.Provenance));
            Assert.That(provenance.GetProperty("profile").GetProperty("module_sha256").GetString(), Is.EqualTo(SmtSolverModule.SHA256));
            Assert.That(provenance.GetProperty("engine").GetProperty("natives_sha256").EnumerateObject().Select(pair => pair.Name), Is.EquivalentTo(new[] { "linux-x64", "osx-arm64", "win-x64" }));
            Assert.That(provenance.GetProperty("controller").GetString(), Is.EqualTo(SmtNodeRig.ControllerVersion));
            Assert.That(provenance.GetProperty("model").GetString(), Is.EqualTo("0.3.0"));
            Assert.That(provenance.GetProperty("options").GetProperty("memory_mb").GetInt32(), Is.EqualTo(64));
            Assert.That(provenance.GetProperty("options").GetProperty("hard_cap_s").GetInt32(), Is.EqualTo(HARD_CAP_SECONDS));
            Assert.That(provenance.GetProperty("problem_set").GetProperty("sha256").GetString(), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(m_set)))));
            Assert.That(provenance.GetProperty("problem_set").GetProperty("rows").GetInt32(), Is.EqualTo(14));
        });
    }

    #endregion

    #region Tools

    private static IEnumerable<string> Lines()
    {
        var many = new StringBuilder("(set-logic QF_LIA)(declare-const x Int)(assert (and (>= x 0) (< x 1000)))");
        for (var index = 0; index < OUTPUT_CONSTANTS; index++)
            many.Append($"(declare-const some_constant_with_a_long_name_{index} Int)(assert (= some_constant_with_a_long_name_{index} {index}))");

        var attributed = $"(set-info :source |{SOURCE}|)(set-info :license \"{LICENSE}\")" + SmtSolverCases.NO_SOLUTION;

        yield return Row(new() { ["id"] = "sat-values", ["smt2"] = SmtSolverCases.LIA, ["values"] = new[] { "x", "(+ x y)" }, ["tags"] = "tagged" });
        yield return Row(new() { ["id"] = "unsat-expected-sat", ["smt2"] = attributed, ["expected"] = "sat" });
        yield return Row(new() { ["id"] = "no-text" });
        yield return Row(new() { ["id"] = "undeclared", ["smt2"] = SmtSolverCases.UNDECLARED });
        yield return Row(new() { ["id"] = "fuel", ["smt2"] = SmtSolverProblems.Pigeonhole(11) });
        yield return Row(new() { ["id"] = "memory", ["smt2"] = SmtSolverProblems.WideMultiplication(2_048) });
        yield return Row(new() { ["id"] = "one-model", ["query"] = "enumerate", ["smt2"] = SmtSolverCases.ONE_SOLUTION, ["project"] = new[] { "x", "y" } });
        yield return Row(new() { ["id"] = "four-models", ["query"] = "enumerate", ["smt2"] = SmtSolverCases.FLAGS, ["project"] = new[] { "v", "flag" }, ["max_models"] = 100 });
        yield return Row(new() { ["id"] = "past-the-cap", ["query"] = "enumerate", ["smt2"] = many.ToString(), ["project"] = new[] { "x" }, ["max_models"] = 1_000 });
        yield return Row(new() { ["id"] = "proved", ["query"] = "prove", ["smt2"] = SmtSolverCases.ABOVE_ONE, ["goal"] = "(> (* x x) x)" });
        yield return Row(new() { ["id"] = "counterexample", ["query"] = "prove", ["smt2"] = SmtSolverCases.ABOVE_ONE, ["goal"] = "(> x 2)" });
        yield return Row(new() { ["id"] = "set-option", ["smt2"] = "(set-option :produce-models true)(declare-const x Int)" });
        yield return Row(new() { ["id"] = "bad id!", ["smt2"] = "(declare-const x Int)" });
        yield return Row(new() { ["id"] = "lost", ["smt2"] = SmtSolverCases.ONE_SOLUTION });
    }

    private static string Row(Dictionary<string, object> fields)
    {
        return JsonSerializer.Serialize(fields);
    }

    #endregion
}
