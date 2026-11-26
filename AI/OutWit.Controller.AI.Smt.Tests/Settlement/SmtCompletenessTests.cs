using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;
using static OutWit.Controller.AI.Smt.Tests.Mock.SmtSettledReader;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The completeness of an enumeration that found models is the solver's last <c>unsat</c>: the
/// evaluator checks the models, and nothing checks that there is no other. On one machine it is
/// that machine's word, as an <c>unsat</c> is, and the documents list it as one: the report's
/// <c>complete_one_copy</c> and the integrity document's count of it, and the enumerations kept
/// as exactly one model that rest on one machine (<c>enumerate.kept_one_copy</c>). Two machines'
/// completeness, and an enumeration that says nothing of its end, are listed nowhere; one that
/// found no model is an <c>unsat</c> and is listed among those. Every document is held to its
/// schema.
/// </summary>
[TestFixture]
public sealed class SmtCompletenessTests
{
    #region Constants

    private const string ONE_X = "(declare-const x Int)(assert (> x 1))(assert (< x 3))";
    private const string NO_X = "(declare-const x Int)(assert (> x 1))(assert (< x 1))";
    private const string X_TWO = "((define-fun x () Int 2))";
    private const string X_TWO_VALUES = "((x 2))";

    #endregion

    #region Fields

    private SmtSettleRig m_rig = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_rig = new SmtSettleRig();
    }

    [TearDown]
    public void TearDown()
    {
        m_rig.Dispose();
    }

    #endregion

    #region Completeness Tests

    [Test]
    public void ACompleteEnumerationOnOneMachineIsListedAsOneMachinesWordTest()
    {
        var (set, options, package) = One(ONE_X, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ => Enumerated([X_TWO], [X_TWO_VALUES], complete: true)) };

        var run = Settle(set, options, [package], returned, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(Row(run, 0).GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED), "its model is checked, its completeness is not");
            Assert.That(Indices(run, "complete_one_copy"), Is.EqualTo(new[] { 0 }), "the report lists it");
            Assert.That(Report(run).GetProperty("complete_one_copy").GetProperty("count").GetInt32(), Is.EqualTo(1));
            Assert.That(Count(run, "complete_one_copy"), Is.EqualTo(1), "the integrity document counts it");
            Assert.That(Indices(run, "enumerate", "kept"), Is.EqualTo(new[] { 0 }), "kept as exactly one model");
            Assert.That(Indices(run, "enumerate", "kept_one_copy"), Is.EqualTo(new[] { 0 }), "on one machine's word");
            Assert.That(Indices(run, "unsat_one_copy"), Is.Empty, "its verdict is sat");
        });
    }

    [Test]
    public void ACompleteEnumerationOfTwoMachinesIsListedNowhereTest()
    {
        var (set, options, package) = One(ONE_X, rate: 1);
        var returned = new[] { m_rig.Answer(package, _ => Enumerated([X_TWO], [X_TWO_VALUES], complete: true)), m_rig.Answer(package, _ => Enumerated([X_TWO], [X_TWO_VALUES], complete: true)) };

        var run = Settle(set, options, [package], returned, 1);

        Assert.Multiple(() =>
        {
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.COPIES));
            Assert.That(Indices(run, "complete_one_copy"), Is.Empty);
            Assert.That(Count(run, "complete_one_copy"), Is.Zero);
            Assert.That(Indices(run, "enumerate", "kept"), Is.EqualTo(new[] { 0 }));
            Assert.That(Indices(run, "enumerate", "kept_one_copy"), Is.Empty);
        });
    }

    [Test]
    public void AnEnumerationThatSaysNothingOfItsEndIsListedNowhereTest()
    {
        // One model, and the session stopped by its budget before it looked for a second: no
        // claim that there is no other.
        var (set, options, package) = One(ONE_X, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ =>
        {
            var row = Enumerated([X_TWO], [X_TWO_VALUES], complete: false);
            row.Reason = SmtResultWords.RLIMIT;
            return row;
        }) };

        var run = Settle(set, options, [package], returned, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
            Assert.That(Indices(run, "complete_one_copy"), Is.Empty);
            Assert.That(Indices(run, "enumerate", "kept_one_copy"), Is.Empty);
        });
    }

    [Test]
    public void ACompleteEnumerationWithoutAModelIsAnUnsatOfOneMachineTest()
    {
        var (set, options, package) = One(NO_X, rate: 0);
        var returned = new[] { m_rig.Answer(package, _ =>
        {
            var row = Enumerated([], [], complete: true);
            row.Verdict = SmtResultWords.UNSAT;
            return row;
        }) };

        var run = Settle(set, options, [package], returned, 0);

        Assert.Multiple(() =>
        {
            Assert.That(Indices(run, "unsat_one_copy"), Is.EqualTo(new[] { 0 }), "listed once, as an unsat");
            Assert.That(Indices(run, "complete_one_copy"), Is.Empty, "and not twice");
            Assert.That(Count(run, "unsat_one_copy"), Is.EqualTo(1));
            Assert.That(Count(run, "complete_one_copy"), Is.Zero);
        });
    }

    #endregion

    #region Tools

    /// <summary>A set of one enumeration projected on x, read with options of a rate, and its one package.</summary>
    private (string Set, SmtOptionsData Options, SmtTaskPackageData Package) One(string smt2, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var line = JsonSerializer.Serialize(new Dictionary<string, object> { ["id"] = "p0", ["query"] = "enumerate", ["smt2"] = smt2, ["project"] = new[] { "x" }, ["max_models"] = 5 });
        var set = SmtSets.Of([line]);
        return (set, options, m_rig.Publish(SmtSettleRig.Read(set, options), options));
    }

    /// <summary>A row of an enumeration with its models and their projected values: complete, or stopped (a test gives the reason).</summary>
    private static SmtResultData Enumerated(List<string> models, List<string> values, bool complete)
    {
        var row = SmtSettleRig.Row(SmtResultWords.OK, models.Count > 0 ? SmtResultWords.SAT : SmtResultWords.UNKNOWN, SmtResultWords.NONE, models, values);
        row.Count = models.Count;
        row.Complete = complete;
        row.Checks = models.Count + 1;
        return row;
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned, double rate)
    {
        var run = m_rig.Settle(set, options, issued, returned, [], [], [], [], rate);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    #endregion
}
