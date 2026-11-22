using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// When an answer may be called wrong, and when a model may be called a witness: the two
/// verdicts that must be right, since one accuses a machine and the other needs no trust.
/// </summary>
[TestFixture]
public class SmtModelValidatorSoundnessTests
{
    #region Constants

    private const string QUANTIFIED = "(forall ((y Int)) (> y 0))";
    private const string ILL_FORMED = "(= (+ 1 true) 2)";

    #endregion

    #region Failed Tests

    [Test]
    public void FalseAssertionFailsWithItsIndexTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (> x 0))(assert (> x 5))(assert (> x 9))", null, "((x 5))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
        Assert.That(validation.Detail, Is.EqualTo("2"));
        Assert.That(validation.Assertions, Is.EqualTo(3));
        Assert.That(validation.True, Is.EqualTo(1));
    }

    [Test]
    public void FalseAssertionFailsWhateverAnotherAssertionLeavesOpenTest()
    {
        // The assertions are separate formulas: one that is false with nothing guessed is
        // false whatever becomes of the others.
        var before = SmtCheck.Validate($"(declare-const x Int)(assert {QUANTIFIED})(assert (> x 5))", null, "((x 5))");
        var after = SmtCheck.Validate($"(declare-const x Int)(assert (> x 5))(assert {QUANTIFIED})", null, "((x 5))");

        Assert.That(before.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(before.Detail, Is.EqualTo("2"));
        Assert.That(after.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(after.Detail, Is.EqualTo("1"));
    }

    [Test]
    public void FalseWithACompletedConstantIsNotFailedTest()
    {
        // The model leaves y out; y is given 0. That the assertion is false with 0 says
        // nothing about the model: another value of y may satisfy it.
        var validation = SmtCheck.Validate("(declare-const y Int)(assert (= y 5))", null);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.COMPLETED_SYMBOL));
    }

    [TestCase("(declare-const y Int)(assert (= y 0))(assert (= y 5))")]
    [TestCase("(declare-const y Int)(define-fun c () Int (+ y 1))(assert (= c 1))(assert (= c 2))")]
    [TestCase("(declare-const y Int)(assert (! (= y 0) :named n))(assert (not n))")]
    [TestCase("(declare-fun f (Int) Int)(assert (= (f 1) 0))(assert (= (f 2) 3))")]
    [TestCase("(declare-const a (Array Int Int))(assert (= (select a 1) 0))(assert (= (select a 2) 3))")]
    [TestCase("(declare-const y Int)(define-fun g ((n Int)) Int (+ n y))(assert (= (g 1) 1))(assert (= (g 1) 2))")]
    public void FalseWithACompletedSymbolReadAgainIsNotFailedTest(string problem)
    {
        // The first assertion reads the completed symbol and holds; the second reads the
        // value again, from what the evaluator remembers, and is false. It rests on the
        // completed symbol all the same.
        var validation = SmtCheck.Validate(problem, null);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.COMPLETED_SYMBOL));
        Assert.That(validation.True, Is.EqualTo(1));
    }

    [Test]
    public void TrueWithCompletedSymbolsIsAWitnessTest()
    {
        // The model with a default value for what it leaves out satisfies every assertion:
        // the problem is satisfiable, whoever said so.
        var validation = SmtCheck.Validate("(declare-const x Int)(declare-const y Int)(declare-fun f (Int) Bool)(assert (>= (+ x y) 0))(assert (not (f x)))", null);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(validation.Defined, Is.EqualTo(0));
    }

    #endregion

    #region Connective Tests

    [TestCase("(and {0} false)")]
    [TestCase("(and false {0})")]
    [TestCase("(and true {0} false true)")]
    [TestCase("(not (or {0} true))")]
    [TestCase("(not (=> {0} true))")]
    [TestCase("(not (=> false {0}))")]
    [TestCase("(and (= (div 1 0) 1) false)")]
    [TestCase("(and (= (hd nil) 1) false)")]
    public void DecidedConnectiveFailsBesideAnUndecidedArgumentTest(string assertion)
    {
        // A quantified formula, a division by zero, a selector on another constructor: each
        // has a truth value under the model that the evaluator does not compute. Whatever it
        // is, the connective is decided by the other argument.
        var validation = SmtCheck.Holds(string.Format(assertion, QUANTIFIED), "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Failed), validation.ToString());
    }

    [TestCase("(or {0} true)")]
    [TestCase("(or true {0})")]
    [TestCase("(=> {0} true)")]
    [TestCase("(=> false {0})")]
    [TestCase("(=> true {0} false true)")]
    [TestCase("(not (and {0} false))")]
    [TestCase("(ite true true {0})")]
    public void DecidedConnectiveHoldsBesideAnUndecidedArgumentTest(string assertion)
    {
        Assert.That(SmtCheck.Holds(string.Format(assertion, QUANTIFIED)).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [TestCase("(or {0} false)")]
    [TestCase("(and {0} true)")]
    [TestCase("(and true {0})")]
    [TestCase("(=> true {0})")]
    [TestCase("(=> {0} false)")]
    [TestCase("(xor {0} true)")]
    [TestCase("(not {0})")]
    [TestCase("(= {0} false)")]
    [TestCase("(ite {0} false false)")]
    public void UndecidedConnectiveIsPartialTest(string assertion)
    {
        var validation = SmtCheck.Holds(string.Format(assertion, QUANTIFIED));

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.QUANTIFIER));
    }

    [TestCase("(and {0} false)")]
    [TestCase("(or {0} true)")]
    [TestCase("(=> {0} true)")]
    [TestCase("(and true (or {0} true) false)")]
    public void ArgumentTheEvaluatorCannotMakeSenseOfLeavesTheAssertionOpenTest(string assertion)
    {
        // Not merely undecided: the term fits no reading, so the problem may not mean what
        // was read. Nothing beside it decides the assertion, neither way.
        var validation = SmtCheck.Holds(string.Format(assertion, ILL_FORMED));

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    [Test]
    public void FalseConjunctBesideACompletedSymbolIsNotFailedTest()
    {
        // The whole assertion is asked whether it rests on a symbol the model left out, not
        // the deciding argument alone: once the symbol was read, the answer is not "wrong".
        var read = SmtCheck.Validate("(declare-const p Bool)(assert (and (not p) false))", null);

        // Here the symbol is never read: the conjunction is decided before it.
        var unread = SmtCheck.Validate("(declare-const p Bool)(assert (and false p))", null);

        Assert.That(read.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(read.Reason, Is.EqualTo(SmtValidationReasons.COMPLETED_SYMBOL));
        Assert.That(unread.Class, Is.EqualTo(SmtValidationClass.Failed));
    }

    [Test]
    public void FalseConjunctBesideABudgetIsNotFailedTest()
    {
        // The first conjunct runs out of steps: nothing more is evaluated, the false
        // conjunct after it included.
        const string problem = "(define-fun-rec loop ((n Int)) Bool (loop (+ n 1)))(assert (and (loop 0) false))";

        var validation = SmtCheck.Validate(problem, null, null, new SmtEvaluationBudget { MaxSteps = 10_000 });

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
    }

    [Test]
    public void IteEvaluatesOnlyTheBranchItsConditionPicksTest()
    {
        Assert.That(SmtCheck.Holds("(= (ite true 1 (div 1 0)) 1)").Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Holds("(= (ite false (div 1 0) 2) 2)").Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Holds("(= (ite false 1 (div 1 0)) 1)").Reason, Is.EqualTo(SmtValidationReasons.DIVISION_BY_ZERO));
    }

    #endregion
}
