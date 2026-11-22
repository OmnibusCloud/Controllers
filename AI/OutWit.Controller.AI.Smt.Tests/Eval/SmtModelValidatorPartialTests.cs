using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// Every reason a validation can be partial for, reached once at least, with its code.
/// </summary>
[TestFixture]
public class SmtModelValidatorPartialTests
{
    #region Constants

    private const string LIST = "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))";

    #endregion

    #region Construct Tests

    [Test]
    public void QuantifierIsPartialTest()
    {
        AssertPartial(SmtCheck.Validate("(declare-const x Int)(assert (forall ((y Int)) (>= (* y y) x)))", null, "((x 0))"), SmtValidationReasons.QUANTIFIER);
        AssertPartial(SmtCheck.Holds("(exists ((y Int)) (= y 1))"), SmtValidationReasons.QUANTIFIER);
    }

    [Test]
    public void AlgebraicNumberIsPartialTest()
    {
        const string problem = "(declare-const x Real)(assert (= (* x x) 2.0))";

        AssertPartial(SmtCheck.Validate(problem, null, "((x (root-obj (+ (^ x 2) (- 2)) 2)))"), SmtValidationReasons.ALGEBRAIC_NUMBER);
    }

    [TestCase("(= (div 1 0) 0)")]
    [TestCase("(= (mod 1 0) 0)")]
    [TestCase("(= (/ 1.0 0.0) 0.0)")]
    public void DivisionByZeroIsPartialTest(string term)
    {
        AssertPartial(SmtCheck.Holds(term), SmtValidationReasons.DIVISION_BY_ZERO);
    }

    [Test]
    public void RegularExpressionIsPartialTest()
    {
        const string problem = "(declare-const s String)(assert (str.in_re s (re.+ (str.to_re \"ab\"))))";

        AssertPartial(SmtCheck.Validate(problem, null, "((s \"abab\"))"), SmtValidationReasons.REGULAR_EXPRESSION);
    }

    [Test]
    public void FloatingPointIsPartialTest()
    {
        const string problem = "(declare-const f (_ FloatingPoint 8 24))(assert (fp.eq f f))";

        AssertPartial(SmtCheck.Validate(problem, null, "((f (fp #b0 #x7f #b00000000000000000000000)))"), SmtValidationReasons.FLOATING_POINT);
        AssertPartial(SmtCheck.Validate("(declare-const f Float32)(assert (= f f))", null, "((f (_ +zero 8 24)))"), SmtValidationReasons.FLOATING_POINT);
        AssertPartial(SmtCheck.Holds("(= RNE roundNearestTiesToEven)"), SmtValidationReasons.FLOATING_POINT);
    }

    [Test]
    public void SelectorOfAnotherConstructorIsPartialTest()
    {
        AssertPartial(SmtCheck.Holds("(= (hd nil) 0)", LIST), SmtValidationReasons.SELECTOR_OF_OTHER_CONSTRUCTOR);
        AssertPartial(SmtCheck.Validate(LIST + "(declare-const l L)(assert (= (tl l) nil))", null, "((l nil))"), SmtValidationReasons.SELECTOR_OF_OTHER_CONSTRUCTOR);
    }

    [TestCase("(= (bvredor #x00) #b0)", "bvredor")]
    [TestCase("(= (^ 2 3) 8)", "^")]
    [TestCase("(= (rem 7 2) 1)", "rem")]
    [TestCase("(= (bv2int #x07) 7)", "bv2int")]
    [TestCase("(bvumul_noovfl #x02 #x02)", "bvumul_noovfl")]
    [TestCase("(= (seq.len (seq.unit 1)) 1)", "seq.")]
    public void OperatorKnownAndNotEvaluatedIsPartialWithItsNameTest(string term, string name)
    {
        var validation = SmtCheck.Holds(term);

        AssertPartial(validation, SmtValidationReasons.UNSUPPORTED_OPERATOR);
        Assert.That(validation.Detail, Is.EqualTo(name));
    }

    [Test]
    public void SortWithoutValuesIsPartialTest()
    {
        AssertPartial(SmtCheck.Validate("(declare-const s (Seq Int))(assert (= s s))", null), SmtValidationReasons.UNSUPPORTED_SORT);
        AssertPartial(SmtCheck.Validate("(declare-const a (Array Int Int Int))(assert (= a a))", null), SmtValidationReasons.UNSUPPORTED_SORT);
        AssertPartial(SmtCheck.Validate("(declare-const x Undeclared)(assert (= x x))", null), SmtValidationReasons.UNSUPPORTED_SORT);
        AssertPartial(SmtCheck.Holds("(= (select (lambda ((x Int) (y Int)) (+ x y)) 1 2) 3)"), SmtValidationReasons.UNSUPPORTED_SORT);
    }

    [Test]
    public void ArraysThatAreNotBothFiniteMapsAreNotComparedTest()
    {
        const string problem = "(declare-const a (Array Int Int))(declare-const b (Array Int Int))(assert (= a b))";
        const string function = "(lambda ((x Int)) (* 2 x))";

        AssertPartial(SmtCheck.Validate(problem, null, $"((a {function}) (b {function}))"), SmtValidationReasons.ARRAY_COMPARISON);
    }

    [Test]
    public void ArraysOverASortTheModelSizesAreNotComparedByTheirDefaultsTest()
    {
        // Every stored index agrees and the defaults differ: whether the arrays differ depends
        // on whether the sort has another element, which is the model's to say.
        const string problem = "(declare-sort U 0)(declare-const a (Array U Int))(declare-const b (Array U Int))(assert (not (= a b)))";
        const string model = "((declare-fun U!val!0 () U) (define-fun a () (Array U Int) (store ((as const (Array U Int)) 0) U!val!0 5)) (define-fun b () (Array U Int) (store ((as const (Array U Int)) 1) U!val!0 5)))";

        AssertPartial(SmtCheck.Validate(problem, model), SmtValidationReasons.ARRAY_COMPARISON);
    }

    [Test]
    public void StringLiteralReadersDisagreeOnIsPartialTest()
    {
        AssertPartial(SmtCheck.Holds("(= (str.len \"\\u{}\") 4)"), SmtValidationReasons.STRING_LITERAL);
    }

    #endregion

    #region Symbol Tests

    [Test]
    public void FalseOnlyWithASymbolTheModelLeftOutIsPartialTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(declare-const y Int)(assert (= x 1))(assert (= y 5))", null, "((x 1))");

        AssertPartial(validation, SmtValidationReasons.COMPLETED_SYMBOL);
        Assert.That(validation.True, Is.EqualTo(1));
        Assert.That(validation.Defined, Is.EqualTo(1));
    }

    [TestCase("(assert (= (+ 1 true) 2))")]
    [TestCase("(assert (and))")]
    [TestCase("(assert (ite true 1))")]
    [TestCase("(assert 5)")]
    [TestCase("(assert (let ((x 1) (x 2)) (= x 1)))")]
    [TestCase("(assert (let ((true false)) true))")]
    [TestCase("(assert ())")]
    [TestCase("(assert :keyword)")]
    [TestCase("(assert (!))")]
    [TestCase("(assert (true))")]
    public void TermTheEvaluatorCannotMakeSenseOfIsPartialTest(string assertion)
    {
        AssertPartial(SmtCheck.Validate(assertion, null), SmtValidationReasons.ILL_FORMED_TERM);
    }

    [TestCase("(assert undeclared)")]
    [TestCase("(assert (undeclared 1))")]
    [TestCase("(assert (= (_ unknown 1) 1))")]
    [TestCase("(assert ((_ unknown 1) 1))")]
    [TestCase("(assert (|forall| 1))")]
    [TestCase("(assert |par|)")]
    [TestCase("(assert (par 1))")]
    public void NameNobodyDeclaresIsPartialTest(string assertion)
    {
        AssertPartial(SmtCheck.Validate(assertion, null), SmtValidationReasons.UNKNOWN_SYMBOL);
    }

    [TestCase("(check-sat)")]
    [TestCase("(push 1)")]
    [TestCase("(declare-fun f)")]
    [TestCase("(assert)")]
    [TestCase("(assert true false)")]
    [TestCase("(define-fun f ((x Int) (x Int)) Int x)")]
    [TestCase("(declare-datatypes ((L 0)) ())")]
    [TestCase("(declare-datatype D ())")]
    [TestCase("(|assert| true)")]
    [TestCase("assert")]
    [TestCase("()")]
    public void CommandTheEvaluatorCannotMakeSenseOfMakesEveryValidationPartialTest(string command)
    {
        AssertPartial(SmtCheck.Validate($"(declare-const x Int){command}(assert (= x 1))", null, "((x 1))"), SmtValidationReasons.ILL_FORMED_COMMAND);
    }

    [TestCase("(declare-const x Int)(declare-const x Int)")]
    [TestCase("(declare-fun f (Int) Int)(declare-fun f (Bool) Int)")]
    [TestCase("(declare-const x Int)(define-fun x () Int 1)")]
    [TestCase("(declare-fun select (Int Int) Int)")]
    [TestCase("(declare-const true Bool)")]
    [TestCase("(define-fun and ((a Bool) (b Bool)) Bool a)")]
    [TestCase("(declare-const |let| Int)")]
    [TestCase("(declare-sort Int 0)")]
    [TestCase("(declare-sort U 0)(define-sort U () Int)")]
    [TestCase("(declare-datatypes ((A 0) (B 0)) (((mk)) ((mk))))")]
    [TestCase("(declare-datatype A ((mk (get Int))))(declare-fun get (A) Int)")]
    [TestCase("(declare-datatype A ((mk)))(declare-fun is-mk (A) Bool)")]
    [TestCase("(declare-const p Bool)(assert (! p :named n))(assert (! p :named n))")]
    public void NameWithTwoMeaningsMakesEveryValidationPartialTest(string commands)
    {
        AssertPartial(SmtCheck.Validate($"{commands}(assert true)", null), SmtValidationReasons.AMBIGUOUS_SYMBOL);
    }

    [TestCase("not a model")]
    [TestCase("(define-fun x () Int 1)")]
    [TestCase("((define-fun x () Int 1)")]
    [TestCase("((define-fun x () Int 1) (define-fun x () Int 2))")]
    [TestCase("((define-fun x Int 1))")]
    [TestCase("((declare-fun f (Int) Int))")]
    [TestCase("((push 1))")]
    [TestCase("() ()")]
    public void TextThatIsNoModelIsPartialTest(string model)
    {
        AssertPartial(SmtCheck.Validate("(declare-const x Int)(assert (= x 1))", model), SmtValidationReasons.UNREADABLE_MODEL);
    }

    #endregion

    #region Tools

    private static void AssertPartial(SmtValidation validation, string reason)
    {
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(reason), validation.ToString());
        Assert.That(SmtValidationReasons.All, Does.Contain(validation.Reason));
    }

    #endregion
}
