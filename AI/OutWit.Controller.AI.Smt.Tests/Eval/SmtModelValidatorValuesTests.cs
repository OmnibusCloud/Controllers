using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The answer of <c>get-value</c> as a claim about the model: every pair of a term and a value
/// is held to the model. A value that is the term's value changes nothing; one that is another
/// value, with nothing guessed, fails the answer; one the evaluator cannot decide leaves it
/// partial; and a text that is no answer of <c>get-value</c> is never dropped unread.
/// </summary>
[TestFixture]
public class SmtModelValidatorValuesTests
{
    #region Constants

    private const string SUM = "(declare-const x Int)(declare-const y Int)(declare-fun f (Int) Int)(assert (= (+ x y) 10))";
    private const string SUM_MODEL = "((define-fun x () Int 4) (define-fun y () Int 6) (define-fun f ((x!0 Int)) Int (ite (= x!0 1) 7 0)))";

    private const string SORTS =
        "(declare-sort U 0)(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))"
        + "(declare-const i Int)(declare-const r Real)(declare-const p Bool)(declare-const v (_ BitVec 8))(declare-const s String)"
        + "(declare-const a (Array Int Int))(declare-const l L)(declare-const u U)(assert (= i (- 5)))";

    private const string SORTS_MODEL =
        "((declare-fun U!val!0 () U) (declare-fun U!val!1 () U) (forall ((x U)) (or (= x U!val!0) (= x U!val!1)))"
        + " (define-fun i () Int (- 5)) (define-fun r () Real (/ 1.0 2.0)) (define-fun p () Bool true) (define-fun v () (_ BitVec 8) #x0f)"
        + " (define-fun s () String \"ab\") (define-fun a () (Array Int Int) (store ((as const (Array Int Int)) 0) 1 5))"
        + " (define-fun l () L (cons 4 nil)) (define-fun u () U U!val!1))";

    #endregion

    #region Term Tests

    [TestCase("(((+ x y) 10))")]
    [TestCase("(((f 1) 7) ((f 2) 0) ((+ x y) 10) (x 4))")]
    [TestCase("(((ite (> x y) x y) 6) ((let ((z (* 2 x))) (+ z 1)) 9) ((= x y) false))")]
    public void ValueThatIsTheValueOfItsTermChangesNothingTest(string values)
    {
        var validation = SmtCheck.Validate(SUM, SUM_MODEL, values);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
        Assert.That(validation.Reason, Is.Null);
        Assert.That(validation.Detail, Is.Null);
    }

    [TestCase("(((+ x y) 12345))", "1")]
    [TestCase("(((f 1) 999))", "1")]
    [TestCase("(((+ x y) 10) ((f 1) 999))", "2")]
    [TestCase("((x 4) (y 6) ((f 2) 1) ((f 1) 999))", "3")]
    [TestCase("(((= x y) true))", "1")]
    public void ValueThatIsNotTheValueOfItsTermFailsTheAnswerTest(string values, string detail)
    {
        // The model satisfies the problem: the answer fails by the value alone.
        var validation = SmtCheck.Validate(SUM, SUM_MODEL, values);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.VALUE_MISMATCH));
        Assert.That(validation.Detail, Is.EqualTo(detail));
        Assert.That(validation.True, Is.EqualTo(validation.Assertions), "every assertion is true under the model");
    }

    #endregion

    #region Constant Tests

    [Test]
    public void ValueOfAConstantDoesNotStandInForTheModelsDefinitionTest()
    {
        // The model says x = 9, which breaks the assertion; a value x = 5 beside it does not mend the model.
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (= x 5))", "((define-fun x () Int 9))", "((x 5))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
        Assert.That(validation.Detail, Is.EqualTo("1"));
    }

    [Test]
    public void ModelThatBreaksTheProblemIsNotMendedByGoodValuesTest()
    {
        // x + y = 10 is false under the model's 0 and 3, whatever the values say of x.
        var validation = SmtCheck.Validate("(declare-const x Int)(declare-const y Int)(assert (= (+ x y) 10))", "((define-fun x () Int 0) (define-fun y () Int 3))", "((x 7))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
    }

    [Test]
    public void ValueOfAConstantThatIsNotTheModelsFailsTheAnswerTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (> x 0))", "((define-fun x () Int 5))", "((x 7))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.VALUE_MISMATCH));
        Assert.That(validation.Detail, Is.EqualTo("1"));
    }

    #endregion

    #region Same Value Tests

    [TestCase("i", "(- 5)")]
    [TestCase("(- i)", "5")]
    [TestCase("r", "(/ 1.0 2.0)")]
    [TestCase("r", "0.5")]
    [TestCase("r", "(/ 2 4)")]
    [TestCase("(* 4 r)", "2.0")]
    [TestCase("(* 4 r)", "2")]
    [TestCase("(- r)", "(- (/ 1.0 2.0))")]
    [TestCase("(to_int r)", "0")]
    [TestCase("p", "true")]
    [TestCase("v", "#x0f")]
    [TestCase("v", "#b00001111")]
    [TestCase("v", "(_ bv15 8)")]
    [TestCase("((_ extract 3 0) v)", "#xf")]
    [TestCase("s", "\"ab\"")]
    [TestCase("(str.++ s \"\"\"\")", "\"ab\"\"\"")]
    [TestCase("a", "(store ((as const (Array Int Int)) 0) 1 5)")]
    [TestCase("a", "(store (store ((as const (Array Int Int)) 0) 1 7) 1 5)")]
    [TestCase("a", "(store (store ((as const (Array Int Int)) 0) 2 0) 1 5)")]
    [TestCase("(store a 1 0)", "((as const (Array Int Int)) 0)")]
    [TestCase("(select a 1)", "5")]
    [TestCase("l", "(cons 4 nil)")]
    [TestCase("(tl l)", "nil")]
    [TestCase("(hd l)", "4")]
    [TestCase("u", "U!val!1")]
    [TestCase("(= u u)", "true")]
    public void OneValueInTwoSpellingsIsTheSameValueTest(string term, string value)
    {
        // A solver and the evaluator may spell one value two ways: no honest answer fails by that.
        var validation = SmtCheck.Validate(SORTS, SORTS_MODEL, $"(({term} {value}))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [TestCase("i", "5")]
    [TestCase("r", "(/ 1.0 3.0)")]
    [TestCase("r", "1")]
    [TestCase("p", "false")]
    [TestCase("v", "#x0e")]
    [TestCase("s", "\"abc\"")]
    [TestCase("a", "(store ((as const (Array Int Int)) 0) 1 6)")]
    [TestCase("a", "(store ((as const (Array Int Int)) 0) 2 5)")]
    [TestCase("a", "((as const (Array Int Int)) 0)")]
    [TestCase("(select a 1)", "0")]
    [TestCase("l", "(cons 5 nil)")]
    [TestCase("l", "nil")]
    [TestCase("u", "U!val!0")]
    public void AnotherValueOfTheSameSortFailsTheAnswerTest(string term, string value)
    {
        var validation = SmtCheck.Validate(SORTS, SORTS_MODEL, $"(({term} {value}))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.VALUE_MISMATCH));
        Assert.That(validation.Detail, Is.EqualTo("1"));
    }

    #endregion

    #region Partial Tests

    [TestCase("(forall ((k Int)) (> k i))", "false", SmtValidationReasons.QUANTIFIER)]
    [TestCase("(div i 0)", "0", SmtValidationReasons.DIVISION_BY_ZERO)]
    [TestCase("(hd nil)", "0", SmtValidationReasons.SELECTOR_OF_OTHER_CONSTRUCTOR)]
    [TestCase("i", "(root-obj (+ (^ x 2) (- 2)) 1)", SmtValidationReasons.ALGEBRAIC_NUMBER)]
    [TestCase("i", "#x05", SmtValidationReasons.ILL_FORMED_TERM)]
    [TestCase("i", "true", SmtValidationReasons.ILL_FORMED_TERM)]
    [TestCase("v", "#x00f", SmtValidationReasons.ILL_FORMED_TERM)]
    [TestCase("(nowhere i)", "0", SmtValidationReasons.UNKNOWN_SYMBOL)]
    [TestCase("i", "(nowhere 1)", SmtValidationReasons.UNKNOWN_SYMBOL)]
    [TestCase("U!val!1", "U!val!1", SmtValidationReasons.UNKNOWN_SYMBOL)]
    public void ValueThatIsNotDecidedLeavesTheAnswerPartialTest(string term, string value, string reason)
    {
        // Neither the same value nor another: the evaluator does not compute the term, cannot
        // read the value, or cannot compare the two. Never validated by silence, never failed.
        var validation = SmtCheck.Validate(SORTS, SORTS_MODEL, $"((i (- 5)) ({term} {value}))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(reason));
    }

    [TestCase("0")]
    [TestCase("5")]
    public void ValueOfATermThatRestsOnASymbolTheModelLeavesOutIsPartialTest(string value)
    {
        // g has no interpretation: whatever the node's solver took for it, the evaluator only guesses.
        const string problem = "(declare-const x Int)(declare-fun g (Int) Int)(assert (> x 0))";

        var validation = SmtCheck.Validate(problem, "((define-fun x () Int 1))", $"((x 1) ((g x) {value}))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.COMPLETED_SYMBOL));
    }

    [TestCase("(((+ x y) (+ x y)))")]
    [TestCase("((x x))")]
    [TestCase("((x (- y 2)))")]
    [TestCase("(((f 1) (f 1)))")]
    [TestCase("(((+ x y) (let ((z x)) (+ z 6))))")]
    public void ValueWrittenWithASymbolOfTheProblemIsNoValueTest(string values)
    {
        // Each pair is true under the model and says nothing: a value names no symbol the problem declares.
        var validation = SmtCheck.Validate(SUM, SUM_MODEL, values);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNREADABLE_VALUES));
    }

    #endregion

    #region Unreadable Tests

    [TestCase("x 4")]
    [TestCase("(x 4)")]
    [TestCase("((x))")]
    [TestCase("((x 4 5))")]
    [TestCase("((x 4)) ((y 6))")]
    [TestCase("((x \"open))")]
    [TestCase("sat")]
    public void TextThatIsNoAnswerOfGetValueLeavesAGoodModelPartialTest(string values)
    {
        var validation = SmtCheck.Validate(SUM, SUM_MODEL, values);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNREADABLE_VALUES));
        Assert.That(validation.True, Is.EqualTo(validation.Assertions), "the model is still checked as the model");
    }

    [Test]
    public void TextThatIsNoAnswerOfGetValueDoesNotHideAModelThatFailsTest()
    {
        var validation = SmtCheck.Validate(SUM, "((define-fun x () Int 4) (define-fun y () Int 7))", "(x 4)");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
    }

    #endregion

    #region Completion Tests

    [Test]
    public void ValueOfAConstantTheModelLeavesOutCompletesTheModelTest()
    {
        // A solver completes its model when it is asked for a value: the value is the constant's.
        const string problem = "(declare-const x Int)(declare-const y Int)(assert (> x 0))(assert (> y 5))";

        var validation = SmtCheck.Validate(problem, "((define-fun x () Int 1))", "((y 7) ((+ x y) 8))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
        Assert.That(validation.Defined, Is.EqualTo(2));
    }

    [Test]
    public void CompletedConstantIsHeldToItsValueLikeADefinedOneTest()
    {
        const string problem = "(declare-const x Int)(declare-const y Int)(assert (> x 0))";
        const string model = "((define-fun x () Int 1))";

        var assertion = SmtCheck.Validate(problem + "(assert (> y 5))", model, "((y 3))");
        var term = SmtCheck.Validate(problem, model, "((y 7) ((+ x y) 9))");
        var twice = SmtCheck.Validate(problem, model, "((y 7) (y 8))");
        var again = SmtCheck.Validate(problem, model, "((y 7) (y 7))");
        var sort = SmtCheck.Validate(problem, model, "((y #x07))");

        Assert.Multiple(() =>
        {
            Assert.That((assertion.Class, assertion.Reason), Is.EqualTo((SmtValidationClass.Failed, SmtValidationReasons.ASSERTION_FALSE)), "the completed value is the model's: an assertion false under it is false");
            Assert.That((term.Class, term.Reason, term.Detail), Is.EqualTo((SmtValidationClass.Failed, SmtValidationReasons.VALUE_MISMATCH, "2")));
            Assert.That((twice.Class, twice.Reason, twice.Detail), Is.EqualTo((SmtValidationClass.Failed, SmtValidationReasons.VALUE_MISMATCH, "2")), "the first value is the constant's, the second is held to it");
            Assert.That(again.Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That((sort.Class, sort.Reason), Is.EqualTo((SmtValidationClass.Partial, SmtValidationReasons.ILL_FORMED_TERM)), "a value of another sort completes nothing, read by an assertion or not");
        });
    }

    [Test]
    public void AnswerOfGetValueAloneIsAModelWhoseOtherPairsAreHeldToItTest()
    {
        const string problem = "(declare-const x Int)(declare-const y Int)(assert (< x y))";

        var right = SmtCheck.Validate(problem, null, "((x 1) (y 2) ((+ x y) 3))");
        var wrong = SmtCheck.Validate(problem, null, "((x 1) (y 2) ((+ x y) 4))");

        Assert.That(right.Class, Is.EqualTo(SmtValidationClass.Validated), right.ToString());
        Assert.That((wrong.Class, wrong.Reason, wrong.Detail), Is.EqualTo((SmtValidationClass.Failed, SmtValidationReasons.VALUE_MISMATCH, "3")));
    }

    #endregion

    #region Budget Tests

    [Test]
    public void ValuesSpendFromTheBudgetOfTheAssertionsTest()
    {
        // The assertion is cheap; the value's term counts down a hundred thousand calls.
        const string problem = "(define-fun-rec down ((n Int)) Int (ite (<= n 0) 0 (down (- n 1))))(declare-const x Int)(assert (> x 0))";
        const string model = "((define-fun x () Int 1))";
        var budget = new SmtEvaluationBudget { MaxSteps = 10_000 };

        var alone = SmtCheck.Validate(problem, model, null, budget);
        var costly = SmtCheck.Validate(problem, model, "(((down 100000) 0))", budget);
        var cheap = SmtCheck.Validate(problem, model, "(((down 10) 0))", budget);

        Assert.Multiple(() =>
        {
            Assert.That(alone.Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That((costly.Class, costly.Reason), Is.EqualTo((SmtValidationClass.Partial, SmtValidationReasons.BUDGET_STEPS)));
            Assert.That(costly.Steps, Is.EqualTo(budget.MaxSteps));
            Assert.That(cheap.Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That(cheap.Steps, Is.GreaterThan(alone.Steps).And.LessThan(budget.MaxSteps));
        });
    }

    [Test]
    public void FalseAssertionStandsBeforeAValueThatIsNotItsTermsTest()
    {
        var validation = SmtCheck.Validate(SUM, "((define-fun x () Int 4) (define-fun y () Int 7))", "((x 5))");

        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
    }

    #endregion
}
