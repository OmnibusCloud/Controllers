using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// Inputs made to run away: each ends as a partial answer with the code of the budget that
/// stopped it, having spent no more than the budget. Nothing here is timed: what bounds a
/// validation is what it counts, and the counts are asserted.
/// </summary>
[TestFixture]
public class SmtModelValidatorBudgetTests
{
    #region Constants

    private const string EXPONENTIAL = "(define-funs-rec ((a ((n Int)) Int) (b ((n Int)) Int)) ((ite (<= n 0) 1 (+ (b (- n 1)) (b (- n 1)))) (ite (<= n 0) 1 (+ (a (- n 1)) (a (- n 1))))))";
    private const string ENDLESS = "(define-fun-rec loop ((n Int)) Int (loop (+ n 1)))";
    private const string ZEROS = "((as const (Array Int Int)) 0)";

    #endregion

    #region Step Tests

    [Test]
    public void DefinitionsThatCallEachOtherTwiceALevelEndAtTheStepBudgetTest()
    {
        // Two to the two hundredth calls, were it left to run.
        var validation = SmtCheck.Validate($"{EXPONENTIAL}(assert (= (a 200) 0))", null);

        AssertBudget(validation, SmtValidationReasons.BUDGET_STEPS, SmtEvaluationBudget.Default);
        Assert.That(validation.Steps, Is.EqualTo(SmtEvaluationBudget.DEFAULT_STEPS));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(1000)]
    [TestCase(250_000)]
    public void StepBudgetIsNeverPassedTest(long steps)
    {
        var budget = new SmtEvaluationBudget { MaxSteps = steps };

        AssertBudget(SmtCheck.Validate($"{EXPONENTIAL}(assert (= (a 200) 0))", null, null, budget), SmtValidationReasons.BUDGET_STEPS, budget);
    }

    [Test]
    public void DefinitionThatNeverEndsEndsAtACeilingTest()
    {
        var deep = SmtCheck.Validate($"{ENDLESS}(assert (= (loop 0) 0))", null);
        var costly = SmtCheck.Validate($"{ENDLESS}(assert (= (loop 0) 0))", null, null, new SmtEvaluationBudget { MaxSteps = 5_000 });

        AssertBudget(deep, SmtValidationReasons.BUDGET_DEPTH, SmtEvaluationBudget.Default);
        Assert.That(costly.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
    }

    [Test]
    public void TermsThatAreNotEvaluatedPayForBeingPassedTest()
    {
        // A hundred thousand disjuncts, none of them evaluated: going on past each costs an
        // exception, and the budget is charged for every one.
        var quantified = SmtCheck.Commands("(forall ((y Int)) (> y 0))").Single();
        var disjuncts = new List<SmtExpression> { SmtExpression.Symbol("or") };
        disjuncts.AddRange(Enumerable.Repeat(quantified, 100_000));
        var assertion = SmtExpression.List(SmtExpression.Symbol("assert"), SmtExpression.List(disjuncts));

        var validation = new SmtModelValidator([assertion]).Validate(SmtCheck.Model(null), SmtEvaluationBudget.Default);

        AssertBudget(validation, SmtValidationReasons.BUDGET_STEPS, SmtEvaluationBudget.Default);
    }

    [Test]
    public void EveryTermCostsAStepTest()
    {
        // No call, no large value: nothing but terms. A flat conjunction of a hundred
        // thousand truths ends at a budget of a thousand steps.
        var conjuncts = new List<SmtExpression> { SmtExpression.Symbol("and") };
        conjuncts.AddRange(Enumerable.Repeat(SmtExpression.Symbol("true"), 100_000));
        var commands = new[] { SmtExpression.List(SmtExpression.Symbol("assert"), SmtExpression.List(conjuncts)) };
        var budget = new SmtEvaluationBudget { MaxSteps = 1000 };

        var flat = new SmtModelValidator(commands).Validate(SmtCheck.Model(null), budget);

        AssertBudget(flat, SmtValidationReasons.BUDGET_STEPS, budget);
        Assert.That(SmtCheck.Holds("true").Steps, Is.EqualTo(1));
        Assert.That(SmtCheck.Holds("(not (not (not false)))").Steps, Is.EqualTo(4));
        Assert.That(SmtCheck.Holds("(ite true (and true true) false)").Steps, Is.EqualTo(5));
    }

    [Test]
    public void CallCostsStepsOfItsOwnTest()
    {
        // Beside the terms it evaluates, a call pays a step for itself and one for each
        // argument: a definition that only passes its argument on is not free.
        const string identity = "(define-fun same ((x Int) (y Int)) Int x)";

        var direct = SmtCheck.Holds("(> 7 0)", identity);
        var called = SmtCheck.Holds("(> (same 7 8) 0)", identity);

        // One for the application, two for its second argument (the term and its digits), one
        // for the body, and three for the call.
        Assert.That(called.Steps - direct.Steps, Is.EqualTo(7));
    }

    [Test]
    public void ValidationIsTheSameEveryTimeTest()
    {
        var first = SmtCheck.Validate($"{EXPONENTIAL}(assert (= (a 12) 4096))(assert (= (a 13) 0))", null);
        var second = SmtCheck.Validate($"{EXPONENTIAL}(assert (= (a 12) 4096))(assert (= (a 13) 0))", null);

        Assert.That(first.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(first.Detail, Is.EqualTo("2"));
        Assert.That(first.Steps, Is.GreaterThan(100_000));
        Assert.That(second.Is(first), Is.True);
    }

    #endregion

    #region Size Tests

    [Test]
    public void StringDoubledFortyTimesEndsAtTheSizeCeilingTest()
    {
        var problem = new StringBuilder("(assert (let ((s0 \"ab\")) ");
        for (var index = 1; index <= 40; index++)
            problem.Append($"(let ((s{index} (str.++ s{index - 1} s{index - 1}))) ");

        problem.Append("(= (str.len s40) 0)").Append(')', 42);

        AssertBudget(SmtCheck.Validate(problem.ToString(), null), SmtValidationReasons.BUDGET_SIZE, SmtEvaluationBudget.Default);
    }

    [TestCase("(= ((_ repeat 1000000000) #x1) #x1)")]
    [TestCase("(= ((_ zero_extend 2000000000) #b1) #b1)")]
    [TestCase("(= ((_ sign_extend 2000000000) #b1) #b1)")]
    [TestCase("(= (_ bv0 2000000000) (_ bv0 2000000000))")]
    [TestCase("(= ((_ int2bv 2000000000) 1) #b1)")]
    [TestCase("(= ((_ repeat 2048) ((_ repeat 2048) #b1)) #b1)")]
    public void BitVectorOfAHugeWidthIsNeverMadeTest(string assertion)
    {
        var validation = SmtCheck.Holds(assertion);

        AssertBudget(validation, SmtValidationReasons.BUDGET_SIZE, SmtEvaluationBudget.Default);
        Assert.That(validation.Steps, Is.LessThan(100_000));
    }

    [Test]
    public void BitVectorDoubledByConcatEndsAtTheSizeCeilingTest()
    {
        var problem = new StringBuilder("(assert (let ((v0 #xff)) ");
        for (var index = 1; index <= 40; index++)
            problem.Append($"(let ((v{index} (concat v{index - 1} v{index - 1}))) ");

        problem.Append("(= v40 v40)").Append(')', 42);

        AssertBudget(SmtCheck.Validate(problem.ToString(), null), SmtValidationReasons.BUDGET_SIZE, SmtEvaluationBudget.Default);
    }

    [Test]
    public void IntegerSquaredOverAndOverEndsAtABudgetTest()
    {
        const string problem = "(define-fun-rec square ((x Int) (n Int)) Int (ite (<= n 0) x (square (* x x) (- n 1))))(assert (> (square 3 60) 0))";

        var byDefault = SmtCheck.Validate(problem, null);
        var bySize = SmtCheck.Validate(problem, null, null, new SmtEvaluationBudget { MaxIntegerBits = 4096 });

        Assert.That(byDefault.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(byDefault.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_SIZE).Or.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        AssertBudget(bySize, SmtValidationReasons.BUDGET_SIZE, SmtEvaluationBudget.Default);
    }

    [Test]
    public void RationalHalvedOverAndOverEndsAtTheSizeCeilingTest()
    {
        const string problem = "(define-fun-rec halve ((x Real) (n Int)) Real (ite (<= n 0) x (halve (/ x 3.0) (- n 1))))(assert (> (halve 1.0 5000) 0.0))";

        AssertBudget(SmtCheck.Validate(problem, null, null, new SmtEvaluationBudget { MaxIntegerBits = 4096 }), SmtValidationReasons.BUDGET_SIZE, SmtEvaluationBudget.Default);
    }

    [Test]
    public void NumeralOfAModelPastTheCeilingIsNeverReadTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (> x 0))", null, $"((x {new string('9', 400_000)}))");

        AssertBudget(validation, SmtValidationReasons.BUDGET_SIZE, SmtEvaluationBudget.Default);
        Assert.That(validation.Steps, Is.LessThan(100_000));
    }

    [Test]
    public void ArrayOfManyStoresEndsAtTheSizeCeilingTest()
    {
        var budget = new SmtEvaluationBudget { MaxArrayEntries = 1000 };

        var at = SmtCheck.Validate($"(assert (= (select {Stores(1000)} 1) 1))", null, null, budget);
        var past = SmtCheck.Validate($"(assert (= (select {Stores(1001)} 1) 1))", null, null, budget);

        Assert.That(at.Class, Is.EqualTo(SmtValidationClass.Validated), at.ToString());
        AssertBudget(past, SmtValidationReasons.BUDGET_SIZE, budget);
    }

    [Test]
    public void ArrayFilledByARecursionEndsAtABudgetTest()
    {
        const string problem = "(define-fun-rec fill ((a (Array Int Int)) (n Int)) (Array Int Int) (ite (<= n 0) a (fill (store a n n) (- n 1))))";

        var validation = SmtCheck.Validate($"{problem}(assert (= (select (fill {ZEROS} 5000000) 1) 1))", null);

        AssertBudget(validation, SmtValidationReasons.BUDGET_DEPTH, SmtEvaluationBudget.Default);
    }

    #endregion

    #region Sort Tests

    [Test]
    public void SortAliasesThatDoubleALevelCostWhatTheyAreWorthTest()
    {
        // A40 written out is two to the fortieth sorts; each alias is expanded once.
        var problem = new StringBuilder("(define-sort A0 () Int)");
        for (var index = 1; index <= 40; index++)
            problem.Append($"(define-sort A{index} () (Array A{index - 1} A{index - 1}))");

        problem.Append("(declare-const x A40)(declare-const z A39)(assert (= x x))(assert (= (select x z) z))");

        var validation = SmtCheck.Validate(problem.ToString(), null);

        // An array is no index of an array: the second assertion is not evaluated, and that is all it costs.
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNSUPPORTED_SORT));
        Assert.That(validation.True, Is.EqualTo(1));
        Assert.That(validation.Steps, Is.LessThan(10_000));
    }

    [Test]
    public void SortNestedDeeperThanSortsNestIsASortWithoutValuesTest()
    {
        var problem = new StringBuilder("(define-sort A0 () Int)");
        for (var index = 1; index <= 80; index++)
            problem.Append($"(define-sort A{index} () (Array Int A{index - 1}))");

        problem.Append("(declare-const x A80)(assert (= x x))");

        Assert.That(SmtCheck.Validate(problem.ToString(), null).Reason, Is.EqualTo(SmtValidationReasons.UNSUPPORTED_SORT));
    }

    [Test]
    public void DatatypeWithoutAValueHasNoDefaultTest()
    {
        const string problem = "(declare-datatypes ((Stream 0)) (((next (head Int) (tail Stream)))))(declare-const s Stream)(assert (= s s))";

        Assert.That(SmtCheck.Validate(problem, null).Reason, Is.EqualTo(SmtValidationReasons.UNSUPPORTED_SORT));
    }

    #endregion

    #region Tools

    private static void AssertBudget(SmtValidation validation, string reason, SmtEvaluationBudget budget)
    {
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(reason), validation.ToString());
        Assert.That(validation.Steps, Is.LessThanOrEqualTo(Math.Max(0, budget.MaxSteps)));
    }

    /// <summary>
    /// So many stores at different indices, one inside the other, over the array of zeros.
    /// </summary>
    private static string Stores(int count)
    {
        var term = new StringBuilder();
        for (var index = 0; index < count; index++)
            term.Append("(store ");

        term.Append(ZEROS);
        for (var index = count; index >= 1; index--)
            term.Append($" {index} {index})");

        return term.ToString();
    }

    #endregion
}
