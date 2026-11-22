using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// What a name stands for where it stands: local names over global ones, the reserved words
/// against the same names between bars, named terms, and the model's own names.
/// </summary>
[TestFixture]
public class SmtModelValidatorScopeTests
{
    #region Constants

    private const string LIST = "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))";

    #endregion

    #region Binder Tests

    [TestCase("(let ((x 5)) (= x 5))")]
    [TestCase("(let ((x 2) (y x)) (= y 1))")]
    [TestCase("(and (let ((x 2)) (= x 2)) (= x 1))")]
    [TestCase("(let ((x 2)) (let ((x (+ x 1))) (= x 3)))")]
    [TestCase("(= (let ((y 10)) (+ x y)) 11)")]
    [TestCase("(let ((y (let ((x 7)) (* x 2)))) (= (+ x y) 15))")]
    [TestCase("(! (= x 1) :pattern ((+ x 1)) :weight 3)")]
    [TestCase("(let ((|let| 4)) (= |let| 4))")]
    public void LetBindsItsVariablesAtOnceAndOnlyForItsBodyTest(string assertion)
    {
        Assert.That(SmtCheck.Validate($"(declare-const x Int)(assert {assertion})", null, "((x 1))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void ParameterHidesAGlobalNameOnlyInItsBodyTest()
    {
        const string problem = "(declare-const x Int)(define-fun twice ((x Int)) Int (* 2 x))(assert (= (twice 5) 10))(assert (= (twice x) 2))(assert (= x 1))";

        Assert.That(SmtCheck.Validate(problem, null, "((x 1))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void NamesOfALetAreTakenBackAfterAnArgumentThatWasNotEvaluatedTest()
    {
        // The first disjunct binds x to 7 and then is not evaluated; the second must see the
        // parameter again, not the 7.
        const string problem = "(define-fun f ((x Int)) Bool (or (let ((x 7)) (= (div x 0) 1)) (= x 3)))(assert (f 3))(assert (not (f 7)))";

        var validation = SmtCheck.Validate(problem, null);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.DIVISION_BY_ZERO));
        Assert.That(validation.True, Is.EqualTo(1));
    }

    [TestCase("(match l ((nil false) ((cons h t) (and (= h 4) (= t nil)))))")]
    [TestCase("(match l (((cons h t) (= h 4)) (nil false)))")]
    [TestCase("(match l ((x (= x l))))")]
    [TestCase("(match l ((nil false) (_ true)))")]
    [TestCase("(match l (((cons _ t) (= t nil)) (_ false)))")]
    [TestCase("(match (tl l) ((nil true) ((cons h t) false)))")]
    [TestCase("(let ((h 9)) (match l (((cons h t) (= h 4)) (nil false))))")]
    [TestCase("(= (match l ((nil 0) ((cons h t) (+ h 1)))) 5)")]
    public void MatchTakesTheFirstCaseThatFitsTest(string assertion)
    {
        Assert.That(SmtCheck.Validate($"{LIST}(declare-const l L)(assert {assertion})", null, "((l (cons 4 nil)))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [TestCase("(match l ((nil false)))")]
    [TestCase("(match l (((cons h) true) (nil false)))")]
    [TestCase("(match l (((cons h h) true) (nil false)))")]
    [TestCase("(match l (((cons true t) true) (nil false)))")]
    [TestCase("(match l ((cons true) (nil false)))")]
    [TestCase("(match 5 ((x true)))")]
    [TestCase("(match l ())")]
    [TestCase("(match l)")]
    public void MatchOutsideItsShapeIsNoValueTest(string assertion)
    {
        var validation = SmtCheck.Validate($"{LIST}(declare-const l L)(assert {assertion})", null, "((l (cons 4 nil)))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    #endregion

    #region Reserved Word Tests

    [Test]
    public void SymbolBetweenBarsIsTheSameSymbolTest()
    {
        // |and| is the symbol and; |x| is x. Only a reserved word changes between bars.
        Assert.That(SmtCheck.Validate("(declare-const |x y| Bool)(assert (|and| |x y| (|=| 1 1)))", null, "((|x y| true))").Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Validate("(declare-const x Bool)(assert |x|)", null, "((x true))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [TestCase("(|let| ((x 1)) true)")]
    [TestCase("(|forall| ((x Int)) true)")]
    [TestCase("(|!| true :named n)")]
    [TestCase("((|_| extract 1 0) #x1)")]
    [TestCase("(|_| bv1 4)")]
    [TestCase("((|as| const (Array Int Int)) 0)")]
    [TestCase("(|match| l ((x true)))")]
    public void ReservedWordBetweenBarsIsNotTheWordTest(string term)
    {
        // The standard has it an ordinary symbol there, and no problem can declare one of
        // that name here: the term is never read as the binder.
        var validation = SmtCheck.Validate($"{LIST}(declare-const l L)(assert (= {term} {term}))", null, "((l nil))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM).Or.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL));
    }

    [Test]
    public void NameOfACommandIsAnOrdinaryNameWithBarsOrWithoutTest()
    {
        // Only the general reserved words shape a term. reset, push, exit and assert are names
        // like any other, and a solver may print a name declared between bars without them:
        // the model's names are matched to the problem's by the name alone.
        const string problem = "(declare-const |assert| Int)(declare-const reset Int)(declare-fun |push| (Int) Int)(declare-fun exit (Int) Int)"
                               + "(assert (= (+ assert |reset|) 3))(assert (= (push |assert|) (|exit| reset)))(assert (let ((|pop| assert) (echo 2)) (= (+ pop |echo|) 3)))";
        const string bare = "((define-fun assert () Int 1) (define-fun reset () Int 2) (define-fun push ((x!0 Int)) Int 7) (define-fun exit ((x!0 Int)) Int (ite (= x!0 reset) 7 0)))";
        const string barred = "((define-fun |assert| () Int 1) (define-fun |reset| () Int 2) (define-fun |push| ((x!0 Int)) Int 7) (define-fun |exit| ((|x!0| Int)) Int (ite (= x!0 |reset|) 7 0)))";

        var byBare = SmtCheck.Validate(problem, bare);
        var byBarred = SmtCheck.Validate(problem, barred);
        var byValues = SmtCheck.Validate(problem, "((define-fun push ((x!0 Int)) Int 7) (define-fun exit ((x!0 Int)) Int 7))", "((|assert| 1) (reset 2))");

        Assert.That(byBare.Class, Is.EqualTo(SmtValidationClass.Validated), byBare.ToString());
        Assert.That(byBarred.Is(byBare), Is.True);
        Assert.That(byValues.Class, Is.EqualTo(SmtValidationClass.Validated), byValues.ToString());
        Assert.That(byBare.Defined, Is.EqualTo(4));
    }

    [TestCase("(let ((let 1)) true)")]
    [TestCase("(let ((_ 1)) true)")]
    [TestCase("(let ((forall 1)) true)")]
    [TestCase("(match l ((par true)))")]
    public void GeneralReservedWordWithoutBarsIsNoNameOfAVariableTest(string assertion)
    {
        var validation = SmtCheck.Validate($"{LIST}(declare-const l L)(assert {assertion})", null, "((l nil))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    #endregion

    #region Named Term Tests

    [Test]
    public void NamedTermIsUsedByItsNameTest()
    {
        const string problem = "(declare-const p Bool)(declare-const q Bool)(assert (! (or p q) :named either))(assert (=> either q))(define-fun both () Bool (! (and p q) :named conjunction))(assert (not conjunction))";

        Assert.That(SmtCheck.Validate(problem, null, "((p false) (q true))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [TestCase("(assert (let ((y 1)) (! (= y 1) :named n)))(assert n)")]
    [TestCase("(define-fun f ((y Int)) Bool (! (= y 1) :named n))(assert (f 1))(assert n)")]
    [TestCase("(assert (not (forall ((y Int)) (! (= y 1) :named n))))(assert n)")]
    public void TermNamedUnderABinderIsNotEvaluatedByItsNameTest(string problem)
    {
        // The name alone does not carry the binder's variables.
        var validation = SmtCheck.Validate($"(declare-const y Int){problem}", null, "((y 5))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.Not.EqualTo(SmtValidationReasons.ASSERTION_FALSE));
    }

    #endregion

    #region Model Name Tests

    [Test]
    public void ProblemDoesNotSeeTheNamesOfTheModelTest()
    {
        // The model's elements and the functions it adds are no part of the problem: a
        // problem that mentions them is one no solver accepted.
        const string model = "((declare-fun U!val!0 () U) (define-fun u () U U!val!0) (define-fun a () (Array Int Int) (_ as-array k!0)) (define-fun k!0 ((x!0 Int)) Int 7))";
        const string declarations = "(declare-sort U 0)(declare-const u U)(declare-const a (Array Int Int))";

        Assert.That(SmtCheck.Validate($"{declarations}(assert (= (select a 3) 7))", model).Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Validate($"{declarations}(assert (= u U!val!0))", model).Reason, Is.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL));
        Assert.That(SmtCheck.Validate($"{declarations}(assert (= (k!0 3) 7))", model).Reason, Is.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL));
        Assert.That(SmtCheck.Validate($"{declarations}(assert (= (select (_ as-array k!0) 3) 7))", model).Reason, Is.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL));
    }

    [Test]
    public void ProblemDefinitionStandsOverAModelDefinitionOfItsNameTest()
    {
        const string problem = "(define-fun c () Int 5)(declare-const x Int)(assert (= x c))";

        Assert.That(SmtCheck.Validate(problem, "((define-fun c () Int 9) (define-fun x () Int 5))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void ValueOfGetValueStandsOverTheDefinitionOfGetModelTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (= x 5))", "((define-fun x () Int 9))", "((x 5))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void ModelThatDefinesItsValuesInACircleIsPartialTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(declare-const y Int)(assert (= x y))", "((define-fun x () Int (+ y 1)) (define-fun y () Int (+ x 1)))");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_DEPTH).Or.EqualTo(SmtValidationReasons.BUDGET_STEPS));
    }

    #endregion
}
