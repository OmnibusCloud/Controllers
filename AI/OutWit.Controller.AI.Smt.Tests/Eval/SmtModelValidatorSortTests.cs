using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// A model is a witness only when it gives every symbol a value of the symbol's own sort. These
/// tests offer models that would "satisfy" a problem with values of another sort: none of them
/// may be validated, and none is a wrong answer by that either.
/// </summary>
[TestFixture]
public class SmtModelValidatorSortTests
{
    #region Constants

    private const string LIST = "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))";
    private const string PAIR = "(declare-datatypes ((Pair 2)) ((par (A B) ((mk (fst A) (snd B))))))";
    private const string COLORS = "(declare-datatype Color ((red) (green)))(declare-datatype Shape ((circle) (square)))";

    #endregion

    #region Constant Tests

    [TestCase("0.5")]
    [TestCase("(/ 1 2)")]
    [TestCase("(/ 1.0 2.0)")]
    [TestCase("1.0")]
    [TestCase("true")]
    [TestCase("#x01")]
    [TestCase("\"1\"")]
    public void IntGivenAnotherSortIsNoWitnessTest(string value)
    {
        // No integer lies strictly between 0 and 1: the problem is unsatisfiable, and a model
        // that says x is a half must not validate it.
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (or (and (< 0 x) (< x 1)) (= x x)))", null, $"((x {value}))");

        AssertNoVerdict(validation);
    }

    [Test]
    public void RealGivenAnIntIsTheRealOfThatNumberTest()
    {
        // The one widening solvers make: an Int where a Real is declared.
        var validation = SmtCheck.Validate("(declare-const r Real)(assert (= (* 2.0 r) 6.0))(assert (is_int r))", null, "((r 3))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [TestCase("(declare-const b (_ BitVec 8))", "#x1")]
    [TestCase("(declare-const b (_ BitVec 8))", "#x0001")]
    [TestCase("(declare-const b (_ BitVec 8))", "1")]
    [TestCase("(declare-const b Bool)", "0")]
    [TestCase("(declare-const b Bool)", "#b1")]
    [TestCase("(declare-const b String)", "5")]
    [TestCase("(declare-const b Real)", "true")]
    [TestCase("(declare-const b Color)", "circle")]
    [TestCase("(declare-const b Color)", "0")]
    [TestCase("(declare-const b L)", "(cons true nil)")]
    [TestCase("(declare-const b L)", "(cons 1.5 nil)")]
    [TestCase("(declare-const b L)", "(cons 1 red)")]
    [TestCase("(declare-const b (Pair Int Bool))", "(mk true 2)")]
    [TestCase("(declare-const b (Pair Int Bool))", "(mk 2.0 true)")]
    [TestCase("(declare-const b (Pair Int (Pair Int Bool)))", "(mk 1 (mk 2 3))")]
    [TestCase("(declare-const b (Array Int Int))", "((as const (Array Int Bool)) true)")]
    [TestCase("(declare-const b (Array Int Int))", "((as const (Array Bool Int)) 0)")]
    [TestCase("(declare-const b (Array Int Int))", "(store ((as const (Array Int Int)) 0) true 1)")]
    [TestCase("(declare-const b (Array Int Int))", "(store ((as const (Array Int Int)) 0) 1 true)")]
    [TestCase("(declare-const b (Array Int Int))", "(lambda ((x Int)) (ite (= x 1) true false))")]
    [TestCase("(declare-const b (Array Int Int))", "(lambda ((x Bool)) 0)")]
    [TestCase("(declare-const b (Array Int Int))", "5")]
    public void ConstantGivenAnotherSortIsNoWitnessTest(string declaration, string value)
    {
        var validation = SmtCheck.Validate($"{LIST}{PAIR}{COLORS}{declaration}(assert (= b b))", null, $"((b {value}))");

        AssertNoVerdict(validation);
    }

    [TestCase("(declare-const b L)", "(cons 1 (cons 2 nil))")]
    [TestCase("(declare-const b (Pair Int Bool))", "(mk 2 true)")]
    [TestCase("(declare-const b (Pair Real (Pair Int Bool)))", "(mk 1.5 (mk 2 false))")]
    [TestCase("(declare-const b (Pair L Color))", "(mk (cons 1 nil) green)")]
    [TestCase("(declare-const b (Array Int Real))", "(store ((as const (Array Int Real)) 0.0) 1 2.5)")]
    [TestCase("(declare-const b (Array Int Real))", "(store ((as const (Array Int Real)) 0) 1 2)")]
    [TestCase("(declare-const b (Array Int (Array Int Bool)))", "((as const (Array Int (Array Int Bool))) ((as const (Array Int Bool)) false))")]
    [TestCase("(declare-const b (Array Int Int))", "(lambda ((x Int)) (ite (= x 1) 5 0))")]
    [TestCase("(declare-const b Color)", "(as green Color)")]
    [TestCase("(declare-const b (Pair Int Bool))", "((as mk (Pair Int Bool)) 2 true)")]
    public void ConstantGivenItsOwnSortIsAWitnessTest(string declaration, string value)
    {
        var validation = SmtCheck.Validate($"{LIST}{PAIR}{COLORS}{declaration}(assert (= b b))", null, $"((b {value}))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
    }

    [Test]
    public void ElementOfAnotherUniverseIsNoWitnessTest()
    {
        const string problem = "(declare-sort U 0)(declare-sort V 0)(declare-const u U)(assert (= u u))";

        var other = SmtCheck.Validate(problem, "((declare-fun V!val!0 () V) (define-fun u () U V!val!0))");
        var missing = SmtCheck.Validate(problem, "((define-fun u () U U!val!0))");
        var own = SmtCheck.Validate(problem, "((declare-fun U!val!0 () U) (define-fun u () U U!val!0))");

        AssertNoVerdict(other);
        Assert.That(missing.Reason, Is.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL));
        Assert.That(own.Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion

    #region Function Tests

    [TestCase("(define-fun f ((x!0 Int)) Int true)")]
    [TestCase("(define-fun f ((x!0 Int)) Int 0.5)")]
    [TestCase("(define-fun f ((x!0 Int)) Bool true)")]
    [TestCase("(define-fun f ((x!0 Bool)) Int 0)")]
    [TestCase("(define-fun f ((x!0 Int) (x!1 Int)) Int 0)")]
    [TestCase("(define-fun f () Int 0)")]
    public void FunctionGivenAnotherSignatureIsNoWitnessTest(string definition)
    {
        var validation = SmtCheck.Validate("(declare-fun f (Int) Int)(assert (= (f 1) (f 1)))", $"({definition})");

        AssertNoVerdict(validation);
    }

    [Test]
    public void FunctionIsCheckedWhereItIsAppliedTest()
    {
        // A function only shows itself at the arguments it is applied to. An interpretation
        // that is of the declared sort there is a witness: any total function that agrees
        // with it at those arguments satisfies the same assertions.
        const string model = "((define-fun f ((x!0 Int)) Int (ite (= x!0 1) 7 (ite (= x!0 2) true 0))))";

        var applied = SmtCheck.Validate("(declare-fun f (Int) Int)(assert (= (f 1) 7))", model);
        var misapplied = SmtCheck.Validate("(declare-fun f (Int) Int)(assert (= (f 1) 7))(assert (= (f 2) (f 2)))", model);

        Assert.That(applied.Class, Is.EqualTo(SmtValidationClass.Validated));
        AssertNoVerdict(misapplied);
    }

    [TestCase("(f 1.5)")]
    [TestCase("(f true)")]
    [TestCase("(f 1 2)")]
    [TestCase("f")]
    [TestCase("(g 1)")]
    [TestCase("(cons 1.5 nil)")]
    [TestCase("(cons 1)")]
    [TestCase("(hd 5)")]
    [TestCase("(hd red)")]
    [TestCase("((_ is cons) red)")]
    [TestCase("((_ is f) nil)")]
    [TestCase("(is-cons 1)")]
    [TestCase("(= red circle)")]
    [TestCase("(= nil red)")]
    [TestCase("(select 5 1)")]
    [TestCase("(select ((as const (Array Int Int)) 0) true)")]
    [TestCase("(store ((as const (Array Int Int)) 0) 1 true)")]
    [TestCase("(= ((as const (Array Int Int)) 0) ((as const (Array Bool Int)) 0))")]
    [TestCase("((as const Int) 0)")]
    [TestCase("(as nil Color)")]
    [TestCase("(as 5 Bool)")]
    public void ProblemTermOutsideTheDeclaredSortsIsNoVerdictTest(string term)
    {
        // A solver refuses such a problem, or converts the value by a rule of its own.
        var declarations = $"{LIST}{COLORS}(declare-fun f (Int) Int)(define-fun g ((n Int)) Bool n)";

        var validation = SmtCheck.Validate($"{declarations}(assert (= {term} {term}))", "((define-fun f ((x!0 Int)) Int 0))");

        AssertNoVerdict(validation);
    }

    [Test]
    public void DefinitionTakesAnIntWhereItDeclaresARealTest()
    {
        const string problem = "(define-fun half ((r Real)) Real (/ r 2.0))(assert (= (half 3) 1.5))(assert (not (is_int (half 3))))";

        Assert.That(SmtCheck.Validate(problem, null).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion

    #region Tools

    private static void AssertNoVerdict(SmtValidation validation)
    {
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM).Or.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL), validation.ToString());
    }

    #endregion
}
