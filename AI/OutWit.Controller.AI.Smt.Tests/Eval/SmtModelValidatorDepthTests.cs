using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The nesting ceiling: a term exactly as deep as the ceiling is evaluated, on the stack the
/// evaluator makes for itself, and a term one level deeper is partial by the count, on every
/// machine alike. The chains real terms are written as cost no nesting at all. The terms are
/// built by a program: no reader's limit stands in the way.
/// </summary>
[TestFixture]
public class SmtModelValidatorDepthTests
{
    #region Constants

    private const string COUNTDOWN = "(define-fun-rec down ((n Int)) Int (ite (<= n 0) 0 (down (- n 1))))";

    /// <summary>The levels under the first call of the countdown, and above the last one's deepest term.</summary>
    private const int COUNTDOWN_OVERHEAD = 6;

    /// <summary>Longer than any problem can be read: the chains are not bounded by the ceiling.</summary>
    private const int CHAIN_LENGTH = 250_000;

    #endregion

    #region Ceiling Tests

    [TestCase("not")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("let")]
    [TestCase("ite")]
    [TestCase("equal")]
    public void TermNestedToTheDefaultCeilingIsEvaluatedAndOneLevelMoreIsNotTest(string shape)
    {
        // The innermost term of n nested forms stands at level n + 1.
        var ceiling = SmtEvaluationBudget.Default.MaxDepth;

        var at = Validate(Nested(shape, ceiling - 1), SmtEvaluationBudget.Default);
        var past = Validate(Nested(shape, ceiling), SmtEvaluationBudget.Default);

        Assert.That(at.Class, Is.Not.EqualTo(SmtValidationClass.Partial), at.ToString());
        Assert.That(past.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(past.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_DEPTH));
    }

    [Test]
    public void RecursionToTheDefaultCeilingIsEvaluatedAndOneCallMoreIsNotTest()
    {
        // A call counts two levels.
        var calls = (SmtEvaluationBudget.Default.MaxDepth - COUNTDOWN_OVERHEAD) / SmtEvaluator.CALL_LEVELS;

        var at = SmtCheck.Validate($"{COUNTDOWN}(assert (= (down {calls}) 0))", null);
        var past = SmtCheck.Validate($"{COUNTDOWN}(assert (= (down {calls + 1}) 0))", null);

        Assert.That(at.Class, Is.EqualTo(SmtValidationClass.Validated), at.ToString());
        Assert.That(past.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(past.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_DEPTH));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(100)]
    [TestCase(5000)]
    public void CeilingOfTheBudgetIsTheLevelTheEvaluationEndsAtTest(int ceiling)
    {
        var budget = new SmtEvaluationBudget { MaxDepth = ceiling };

        Assert.That(Validate(Nested("not", ceiling - 1), budget).Class, Is.Not.EqualTo(SmtValidationClass.Partial));
        Assert.That(Validate(Nested("not", ceiling), budget).Reason, Is.EqualTo(SmtValidationReasons.BUDGET_DEPTH));
    }

    [Test]
    public void CeilingAboveTheHighestIsTakenAsTheHighestTest()
    {
        var budget = new SmtEvaluationBudget { MaxDepth = int.MaxValue };

        var at = Validate(Nested("not", SmtEvaluationBudget.DEPTH_CEILING - 1), budget);
        var past = Validate(Nested("not", SmtEvaluationBudget.DEPTH_CEILING), budget);

        Assert.That(at.Class, Is.Not.EqualTo(SmtValidationClass.Partial), at.ToString());
        Assert.That(past.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(past.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_DEPTH));
    }

    [Test]
    public void DefaultCeilingIsAboveTheDeepestProblemThatCanBeReadTest()
    {
        // Whatever shape a problem nests in, its own nesting does not end a validation.
        Assert.That(SmtEvaluationBudget.Default.MaxDepth, Is.GreaterThan(SmtReaderLimits.Problem.MaxDepth));
        Assert.That(SmtEvaluationBudget.Default.MaxDepth, Is.GreaterThan(100_000));
    }

    #endregion

    #region Chain Tests

    [TestCase("let")]
    [TestCase("ite")]
    [TestCase("then")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("implies")]
    [TestCase("annotation")]
    [TestCase("match")]
    public void ChainThatRealTermsNestWithoutEndCostsNoNestingTest(string shape)
    {
        // Tools write shared terms as chains of let many thousands long, and a solver prints a
        // function as a chain of ite as long as its table: the evaluator goes round them. A
        // quarter of a million links are evaluated under a ceiling of sixteen levels.
        var budget = new SmtEvaluationBudget { MaxDepth = 16 };
        var declarations = SmtCheck.Commands("(declare-datatype Unit ((unit)))");
        var assertion = SmtExpression.List(SmtExpression.Symbol("assert"), Chain(shape, CHAIN_LENGTH));

        var validation = new SmtModelValidator([.. declarations, assertion]).Validate(SmtCheck.Model(null), budget);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [Test]
    public void ProblemWrittenAsAChainOfLetIsEvaluatedWhateverItsLengthTest()
    {
        // Each let names a term that uses the one before, as a tool writes them; the body of
        // the last one compares the result. The chain is deeper than the nesting ceiling.
        const int length = 150_000;
        var symbol = SmtExpression.Symbol;
        var body = SmtCheck.Commands($"(and (= v{length} (+ x {length})) (> v{length / 2} x))").Single();
        for (var index = length; index >= 1; index--)
        {
            var previous = index == 1 ? symbol("x") : symbol($"v{index - 1}");
            var value = SmtExpression.List(symbol("+"), previous, SmtExpression.Atom(SmtExpressionKind.Numeral, "1"));
            body = SmtExpression.List(symbol("let"), SmtExpression.List(SmtExpression.List(symbol($"v{index}"), value)), body);
        }

        var commands = new List<SmtExpression>(SmtCheck.Commands("(declare-const x Int)")) { SmtExpression.List(symbol("assert"), body) };

        var validation = new SmtModelValidator(commands).Validate(SmtCheck.Model(null, "((x 7))"), SmtEvaluationBudget.Default);

        Assert.That(length, Is.GreaterThan(SmtEvaluationBudget.Default.MaxDepth));
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [Test]
    public void ChainOfConnectivesWithAnUndecidedLinkIsStillDecidedByItsEndTest()
    {
        // (and Q (and Q ... false)): every link has an argument that is not evaluated, and the
        // end of the chain decides all of it.
        var quantified = SmtCheck.Commands("(forall ((y Int)) (> y 0))").Single();
        var term = SmtExpression.Symbol("false");
        for (var link = 0; link < 200; link++)
            term = SmtExpression.List(SmtExpression.Symbol("and"), quantified, term);

        Assert.That(Validate(term, SmtEvaluationBudget.Default).Class, Is.EqualTo(SmtValidationClass.Failed));
    }

    [TestCase("(and true 5)")]
    [TestCase("(or false 5)")]
    [TestCase("(=> true 5)")]
    [TestCase("(= (and true 5) 5)")]
    [TestCase("(= (or false (ite true 5 6)) 5)")]
    [TestCase("(and true (let ((x 5)) x))")]
    public void LastArgumentOfAConnectiveIsATruthValueLikeTheOthersTest(string assertion)
    {
        // The last argument is walked in the connective's place; it is checked all the same.
        var validation = SmtCheck.Holds(assertion);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    [Test]
    public void FunctionWithATableOfAHundredThousandRowsIsAppliedWithoutNestingTest()
    {
        var rows = new StringBuilder();
        const int count = 100_000;
        for (var row = 0; row < count; row++)
            rows.Append($"(ite (= x!0 {row}) {row + 1} ");

        var model = $"((define-fun f ((x!0 Int)) Int {rows}0{new string(')', count)}))";

        var validation = SmtCheck.Validate("(declare-fun f (Int) Int)(assert (= (f 99999) 100000))(assert (= (f (- 1)) 0))", model, null, new SmtEvaluationBudget { MaxDepth = 16 });

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [Test]
    public void LongListsAreComparedWithoutNestingTest()
    {
        // Two lists of three thousand elements, each built one element a definition, so that
        // nothing nests while they are built; then compared under a ceiling a hundred times
        // lower than their length. Equality goes round the tail of a list.
        const int length = 3000;
        var problem = new StringBuilder("(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))(define-fun a0 () L nil)(define-fun b0 () L nil)");
        for (var index = 1; index <= length; index++)
            problem.Append($"(define-fun a{index} () L (cons {index} a{index - 1}))(define-fun b{index} () L (cons {index} b{index - 1}))(assert (= (hd a{index}) (hd b{index})))");

        problem.Append($"(assert (= a{length} b{length}))(assert (not (= a{length} (cons 0 b{length}))))(assert (not (= a{length} b{length - 1})))");

        var validation = SmtCheck.Validate(problem.ToString(), null, null, new SmtEvaluationBudget { MaxDepth = 32 });

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    #endregion

    #region Tools

    private static SmtValidation Validate(SmtExpression term, SmtEvaluationBudget budget)
    {
        var assertion = SmtExpression.List(SmtExpression.Symbol("assert"), term);
        return new SmtModelValidator([assertion]).Validate(SmtCheck.Model(null), budget);
    }

    /// <summary>
    /// A Boolean term of so many forms, each inside the one before, in a place that is walked
    /// by a call: an argument, a condition, the value of a variable, a connective's argument
    /// that is not its last.
    /// </summary>
    private static SmtExpression Nested(string shape, int levels)
    {
        var symbol = SmtExpression.Symbol;
        var term = symbol("true");
        for (var level = 0; level < levels; level++)
        {
            term = shape switch
            {
                "not" => SmtExpression.List(symbol("not"), term),
                "and" => SmtExpression.List(symbol("and"), term, symbol("true")),
                "or" => SmtExpression.List(symbol("or"), term, symbol("false")),
                "ite" => SmtExpression.List(symbol("ite"), term, symbol("true"), symbol("false")),
                "equal" => SmtExpression.List(symbol("="), symbol("true"), term),
                _ => SmtExpression.List(symbol("let"), SmtExpression.List(SmtExpression.List(symbol("x"), term)), symbol("x"))
            };
        }

        return term;
    }

    /// <summary>
    /// A true term of so many forms, each the last part of the one before: the place that is
    /// walked by going round.
    /// </summary>
    private static SmtExpression Chain(string shape, int length)
    {
        var symbol = SmtExpression.Symbol;
        var term = symbol("true");
        for (var link = 0; link < length; link++)
        {
            term = shape switch
            {
                "let" => SmtExpression.List(symbol("let"), SmtExpression.List(SmtExpression.List(symbol("x"), symbol("true"))), term),
                "ite" => SmtExpression.List(symbol("ite"), symbol("false"), symbol("false"), term),
                "then" => SmtExpression.List(symbol("ite"), symbol("true"), term, symbol("false")),
                "and" => SmtExpression.List(symbol("and"), symbol("true"), symbol("true"), term),
                "or" => SmtExpression.List(symbol("or"), symbol("false"), term),
                "implies" => SmtExpression.List(symbol("=>"), symbol("true"), term),
                "match" => SmtExpression.List(symbol("match"), symbol("unit"), SmtExpression.List(SmtExpression.List(symbol("unit"), term))),
                _ => SmtExpression.List(symbol("!"), term, SmtExpression.Atom(SmtExpressionKind.Keyword, ":weight"), SmtExpression.Atom(SmtExpressionKind.Numeral, "1"))
            };
        }

        return term;
    }

    #endregion
}
