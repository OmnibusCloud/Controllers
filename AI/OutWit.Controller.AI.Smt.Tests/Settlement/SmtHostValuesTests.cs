using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The values a node reports are a claim the host checks. The host knows the terms a row asked
/// the values of (they travel with the problem) and holds the node's text to them: as many pairs,
/// each of its term, each value the value the term has under the row's model. A value that is
/// another fails the row and sends its problem to three more machines; values that are not those
/// asked, or that the evaluator cannot decide, leave the row partial; nothing is validated
/// unread. The rows are made up as a node writes them, without nodes and without the solver.
/// </summary>
[TestFixture]
public sealed class SmtHostValuesTests
{
    #region Constants

    private const string SUM = "(declare-const x Int)(declare-const y Int)(declare-fun f (Int) Int)(assert (= (+ x y) 10))";
    private const string SUM_MODEL = "((define-fun x () Int 4) (define-fun y () Int 6) (define-fun f ((x!0 Int)) Int (ite (= x!0 1) 7 0)))";
    private const string SUM_VALUES = "((x 4) ((+ x y) 10) ((f 1) 7))";

    private const string PAIRS = "(declare-const x Int)(declare-const y Int)(assert (<= 0 x 1))(assert (= y (+ x 1)))";
    private const string PAIR_ZERO = "((define-fun x () Int 0) (define-fun y () Int 1))";
    private const string PAIR_ONE = "((define-fun x () Int 1) (define-fun y () Int 2))";

    private static readonly string[] SUM_ASKED = ["x", "(+ x y)", "(f 1)"];

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

    #region Check Tests

    [Test]
    public void ValuesThatAreTheValuesOfTheTermsAskedLeaveTheRowValidatedTest()
    {
        var check = SmtHostChecks.Check(Sum(), SmtSettleRig.Sat(SUM_MODEL, SUM_VALUES));

        Assert.That(check, Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
    }

    [TestCase("((x 5) ((+ x y) 10) ((f 1) 7))", "1")]
    [TestCase("((x 4) ((+ x y) 12345) ((f 1) 7))", "2")]
    [TestCase("((x 4) ((+ x y) 10) ((f 1) 999))", "3")]
    [TestCase("((x 4) ((+ x y) 12345) ((f 1) 999))", "2")]
    public void ValueThatIsNotTheValueOfItsTermFailsTheRowTest(string values, string detail)
    {
        // The node solved honestly and wrote another value beside the true model.
        var check = SmtHostChecks.Check(Sum(), SmtSettleRig.Sat(SUM_MODEL, values));

        Assert.That(check, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.VALUE_MISMATCH, detail)));
    }

    [Test]
    public void ModelThatBreaksTheProblemIsNotMendedByValuesThatSatisfyItTest()
    {
        // x + y = 10 is false under the model's 0 and 3; the values say x = 7.
        var check = SmtHostChecks.Check(Sum(), SmtSettleRig.Sat("((define-fun x () Int 0) (define-fun y () Int 3) (define-fun f ((x!0 Int)) Int 7))", "((x 7) ((+ x y) 10) ((f 1) 7))"));

        Assert.That(check, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.ASSERTION_FALSE, "1")));
    }

    [TestCase("((x 4))", "fewer")]
    [TestCase("((x 4) ((+ x y) 10) ((f 1) 7) (y 6))", "more")]
    [TestCase("((x 4) ((+ x y) 10) ((f 2) 0))", "another term")]
    [TestCase("((x 4) ((+ y x) 10) ((f 1) 7))", "a term written another way")]
    [TestCase("(((+ x y) 10) (x 4) ((f 1) 7))", "another order")]
    [TestCase("()", "none in the list")]
    [TestCase("(x 4)", "no pairs")]
    [TestCase("sat", "no list")]
    [TestCase("((x 4) ((+ x y) 10) ((f 1) 7)", "a list left open")]
    public void ValuesThatAreNotThoseOfTheTermsAskedLeaveTheRowPartialTest(string values, string what)
    {
        // Each of these would be right, or unread, if the host took the node's word for what was asked.
        var check = SmtHostChecks.Check(Sum(), SmtSettleRig.Sat(SUM_MODEL, values));

        Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_VALUES, "")), what);
    }

    [Test]
    public void RowAskedForValuesThatGivesNoneIsPartialTest()
    {
        var check = SmtHostChecks.Check(Sum(), SmtSettleRig.Sat(SUM_MODEL));

        Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_VALUES, "")));
    }

    [Test]
    public void RowNotAskedForValuesThatGivesSomeIsPartialTest()
    {
        var problem = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["smt2"] = SUM }));

        var without = SmtHostChecks.Check(problem, SmtSettleRig.Sat(SUM_MODEL));
        var with = SmtHostChecks.Check(problem, SmtSettleRig.Sat(SUM_MODEL, "((x 4))"));

        Assert.That(without, Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
        Assert.That(with, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_VALUES, "")));
    }

    [Test]
    public void ValuesThatAreNotThoseAskedDoNotHideAModelThatFailsTest()
    {
        var check = SmtHostChecks.Check(Sum(), SmtSettleRig.Sat("((define-fun x () Int 4) (define-fun y () Int 7) (define-fun f ((x!0 Int)) Int 7))", "(x 4)"));

        Assert.That(check, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.ASSERTION_FALSE, "1")));
    }

    [Test]
    public void ValueOfATermTheEvaluatorDoesNotComputeLeavesAnHonestRowPartialTest()
    {
        // The solver gives a division by zero a value of its own; the evaluator does not compute
        // one, and says so where it used to say nothing.
        var problem = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["smt2"] = SUM, ["values"] = new[] { "x", "(div x 0)" } }));

        var check = SmtHostChecks.Check(problem, SmtSettleRig.Sat(SUM_MODEL, "((x 4) ((div x 0) 0))"));

        Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.DIVISION_BY_ZERO, "")));
    }

    #endregion

    #region Enumeration Tests

    [Test]
    public void ProjectedValuesOfEveryModelOfAnEnumerationAreHeldToThatModelTest()
    {
        var problem = Pairs();

        var honest = SmtHostChecks.Check(problem, Enumerated([PAIR_ZERO, PAIR_ONE], ["((x 0) (y 1))", "((x 1) (y 2))"]));
        var swapped = SmtHostChecks.Check(problem, Enumerated([PAIR_ZERO, PAIR_ONE], ["((x 0) (y 1))", "((x 0) (y 1))"]));
        var forged = SmtHostChecks.Check(problem, Enumerated([PAIR_ZERO, PAIR_ONE], ["((x 0) (y 1))", "((x 1) (y 7))"]));
        var first = SmtHostChecks.Check(problem, Enumerated([PAIR_ZERO, PAIR_ONE], ["((x 5) (y 1))", "((x 1) (y 2))"]));

        Assert.Multiple(() =>
        {
            Assert.That(honest, Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
            Assert.That(swapped, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.VALUE_MISMATCH, "model 2: 1")), "the values of another model");
            Assert.That(forged, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.VALUE_MISMATCH, "model 2: 2")));
            Assert.That(first, Is.EqualTo((SmtRunWords.FAILED, SmtValidationReasons.VALUE_MISMATCH, "model 1: 1")));
        });
    }

    [TestCase("((x 1))", "one constant of two")]
    [TestCase("((y 2) (x 1))", "the constants in another order")]
    [TestCase("((x 1) ((+ x 1) 2))", "a term in place of a constant")]
    [TestCase("x 1", "no answer of get-value")]
    public void ProjectedValuesThatAreNotThoseOfTheConstantsNamedLeaveTheRowPartialTest(string values, string what)
    {
        var check = SmtHostChecks.Check(Pairs(), Enumerated([PAIR_ZERO, PAIR_ONE], ["((x 0) (y 1))", values]));

        Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_VALUES, "")), what);
    }

    [Test]
    public void ProjectedConstantIsNamedAsTheProblemDeclaresItTest()
    {
        // A name between bars: the node's session names the constant as the problem's declaration does.
        var problem = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["query"] = "enumerate", ["smt2"] = "(declare-const |a b| Int)(assert (= |a b| 3))", ["project"] = new[] { "|a b|" } }));

        var check = SmtHostChecks.Check(problem, Enumerated(["((define-fun |a b| () Int 3))"], ["((|a b| 3))"]));

        Assert.That(check, Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
    }

    #endregion

    #region Proof Tests

    [Test]
    public void CounterexampleOfAProofComesWithoutValuesTest()
    {
        var problem = SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["query"] = "prove", ["smt2"] = "(declare-const x Int)(assert (> x 0))", ["goal"] = "(> x 1)" }));
        const string counterexample = "((define-fun x () Int 1))";

        var without = SmtHostChecks.Check(problem, SmtSettleRig.Sat(counterexample));
        var with = SmtHostChecks.Check(problem, SmtSettleRig.Sat(counterexample, "((x 1))"));

        Assert.That(without, Is.EqualTo((SmtRunWords.VALIDATED, "", "")));
        Assert.That(with, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_VALUES, "")), "no session of a proof asks for values");
    }

    #endregion

    #region Guard Tests

    [Test]
    public void RowTheEvaluatorCannotReadIsPartialAndNotTheEndOfTheSettlementTest()
    {
        // The intake refuses such a row; the evaluator behind it still answers for it.
        var models = SmtSettleRig.Sat(SUM_MODEL, SUM_VALUES);
        models.Models = null!;
        var values = SmtSettleRig.Sat(SUM_MODEL, SUM_VALUES);
        values.Values = null!;
        var model = SmtSettleRig.Sat(SUM_MODEL, SUM_VALUES);
        model.Models = [null!];

        Assert.Multiple(() =>
        {
            Assert.That(SmtHostChecks.Check(Sum(), models), Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.EVALUATOR_ERROR, "")));
            Assert.That(SmtHostChecks.Check(Sum(), values), Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.EVALUATOR_ERROR, "")));
            Assert.That(SmtHostChecks.Check(Sum(), model).Class, Is.EqualTo(SmtRunWords.PARTIAL), "a model that is not there is no model");
        });
    }

    [TestCase(SmtResultWords.REFUSED)]
    [TestCase(SmtResultWords.INPUT_ERROR)]
    [TestCase(SmtResultWords.ENGINE_CRASH)]
    [TestCase(SmtResultWords.NODE_FAULT)]
    public void SatUnderAStatusThatHasNoVerdictIsPartialNeverNoneTest(string status)
    {
        // No session says sat with such a status: what the row holds is no model to take on its word.
        var row = SmtSettleRig.Row(status, SmtResultWords.SAT, SmtResultWords.NONE, [SUM_MODEL], [SUM_VALUES]);

        var check = SmtHostChecks.Check(Sum(), row);

        Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "")));
    }

    [Test]
    public void RowThatDoesNotSaySatHasNoModelToCheckTest()
    {
        var unsat = SmtHostChecks.Check(Sum(), SmtSettleRig.Unsat());
        var refused = SmtHostChecks.Check(Sum(), SmtSettleRig.Row(SmtResultWords.INPUT_ERROR, SmtResultWords.NONE, SmtResultWords.NONE, [], []));
        var exceeded = SmtHostChecks.Check(Sum(), SmtSettleRig.Row(SmtResultWords.OUTPUT_EXCEEDED, SmtResultWords.SAT, SmtResultWords.NONE, [], []));

        Assert.Multiple(() =>
        {
            Assert.That(unsat, Is.EqualTo((SmtRunWords.NOT_CHECKED, "", "")));
            Assert.That(refused, Is.EqualTo((SmtRunWords.NOT_CHECKED, "", "")));
            Assert.That(exceeded, Is.EqualTo((SmtRunWords.PARTIAL, SmtRunWords.OUTPUT_EXCEEDED, "")), "a model that did not fit the session's output");
        });
    }

    #endregion

    #region Run Tests

    [Test]
    public void RowWithAForgedValueGoesToThreeMoreMachinesAndIsSettledAgainstItTest()
    {
        // One copy, not sampled: the forged values would stand beside "validated" on one machine's word.
        var (set, options, package) = One(Line(new() { ["id"] = "p0", ["smt2"] = SUM, ["values"] = SUM_ASKED }));
        const string forged = "((x 4) ((+ x y) 12345) ((f 1) 999))";
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(SUM_MODEL, forged)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1), "a row with a value that is not its term's is never taken on one copy");

        var settled = new List<SmtResultPackageData>();
        for (var copy = 0; copy < 3; copy++)
            settled.Add(m_rig.Answer(disputes[0], _ => SmtSettleRig.Sat(SUM_MODEL, SUM_VALUES)));

        var unsettled = Settle(set, options, [package], returned, disputes, []);
        var run = Settle(set, options, [package], returned, disputes, settled);
        var row = Row(run);
        var integrity = JsonDocument.Parse(run.Integrity).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(Row(unsettled).GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.DISPUTED), "without the three more copies the forged row is no answer");

            Assert.That(row.GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.DISPUTE));
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.VALIDATED));
            Assert.That(row.GetProperty("values").GetString(), Is.EqualTo(SUM_VALUES), "the values of the settled answer");
            Assert.That(integrity.GetProperty("disputes")[0].GetProperty("cause").GetString(), Is.EqualTo(SmtRunWords.MODEL_FAILED));
            Assert.That(integrity.GetProperty("summary").GetProperty("lies").GetInt32(), Is.EqualTo(1), "the copy that differs from the settled answer of the others");
        });
    }

    [Test]
    public void RowWhoseValuesAreNotThoseAskedIsWrittenPartialWithItsReasonTest()
    {
        var (set, options, package) = One(Line(new() { ["id"] = "p0", ["smt2"] = SUM, ["values"] = SUM_ASKED }));
        const string others = "((x 4) ((+ x y) 10) ((f 2) 0))";
        var returned = new[] { m_rig.Answer(package, _ => SmtSettleRig.Sat(SUM_MODEL, others)) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        var run = Settle(set, options, [package], returned, disputes, []);
        var row = Row(run);
        var reasons = JsonDocument.Parse(run.Report).RootElement.GetProperty("validation").GetProperty("partial_by_reason");

        Assert.Multiple(() =>
        {
            Assert.That(disputes, Is.Empty, "a partial row accuses nobody");
            Assert.That(row.GetProperty("validation").GetString(), Is.EqualTo(SmtRunWords.PARTIAL));
            Assert.That(row.GetProperty("validation_reason").GetString(), Is.EqualTo(SmtValidationReasons.UNREADABLE_VALUES));
            Assert.That(row.GetProperty("values").GetString(), Is.EqualTo(others), "the node's text is shown beside the word that says it was not taken");
            Assert.That(reasons.GetProperty(SmtValidationReasons.UNREADABLE_VALUES).GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public void EnumerationWithTheValuesOfAnotherModelGoesToThreeMoreMachinesTest()
    {
        var (set, options, package) = One(Line(new() { ["id"] = "p0", ["query"] = "enumerate", ["smt2"] = PAIRS, ["project"] = new[] { "x", "y" }, ["max_models"] = 5 }));
        var returned = new[] { m_rig.Answer(package, _ => Enumerated([PAIR_ZERO, PAIR_ONE], ["((x 0) (y 1))", "((x 0) (y 1))"])) };

        var disputes = m_rig.Disputes([package], returned, [], []);
        var run = Settle(set, options, [package], returned, disputes, []);

        Assert.That(disputes.Sum(dispute => dispute.ProblemCount), Is.EqualTo(1));
        Assert.That(Row(run).GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.DISPUTED));
    }

    #endregion

    #region Tools

    /// <summary>The check that asks for the values of three terms.</summary>
    private static SmtProblemData Sum()
    {
        return SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["smt2"] = SUM, ["values"] = SUM_ASKED }));
    }

    /// <summary>The enumeration of two models, told apart by two constants.</summary>
    private static SmtProblemData Pairs()
    {
        return SmtHostChecks.Problem(Line(new() { ["id"] = "p0", ["query"] = "enumerate", ["smt2"] = PAIRS, ["project"] = new[] { "x", "y" }, ["max_models"] = 5 }));
    }

    /// <summary>A row of an enumeration that found every model there is.</summary>
    private static SmtResultData Enumerated(List<string> models, List<string> values)
    {
        var row = SmtSettleRig.Row(SmtResultWords.OK, SmtResultWords.SAT, SmtResultWords.NONE, models, values);
        row.Count = models.Count;
        row.Complete = true;
        row.Checks = models.Count + 1;
        return row;
    }

    private static string Line(Dictionary<string, object> row)
    {
        return JsonSerializer.Serialize(row);
    }

    /// <summary>A set of one row, read at a sampling rate of zero, and its one package.</summary>
    private (string Set, SmtOptionsData Options, SmtTaskPackageData Package) One(string line)
    {
        var options = new SmtOptionsData { SamplingRate = 0 };
        var set = SmtSets.Of([line]);
        return (set, options, m_rig.Publish(SmtSettleRig.Read(set, options), options));
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> disputes, IReadOnlyList<SmtResultPackageData> settled)
    {
        var run = m_rig.Settle(set, options, issued, returned, [], [], disputes, settled, 0);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    private static JsonElement Row(SmtSettledDocuments run)
    {
        return run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).Single();
    }

    #endregion
}
