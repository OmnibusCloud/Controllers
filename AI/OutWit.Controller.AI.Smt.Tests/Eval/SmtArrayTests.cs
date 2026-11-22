using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtArrayTests
{
    #region Constants

    private const string ZEROS = "((as const (Array Int Int)) 0)";

    #endregion

    #region Read Tests

    [TestCase("(= (select {0} 5) 0)")]
    [TestCase("(= (select (store {0} 5 7) 5) 7)")]
    [TestCase("(= (select (store {0} 5 7) 6) 0)")]
    [TestCase("(= (select (store (store {0} 5 7) 5 8) 5) 8)")]
    [TestCase("(= (select (store (store {0} 5 7) 6 8) 5) 7)")]
    [TestCase("(= (select (store {0} (- 3) 7) (- 5 8)) 7)")]
    [TestCase("(= (select (select (store ((as const (Array Int (Array Int Int))) {0}) 1 (store {0} 2 3)) 1) 2) 3)")]
    [TestCase("(= (select (lambda ((i Int)) (* i i)) 7) 49)")]
    [TestCase("(= (select (store (lambda ((i Int)) (* i i)) 7 0) 7) 0)")]
    [TestCase("(= (select (store (lambda ((i Int)) (* i i)) 7 0) 3) 9)")]
    [TestCase("(let ((k 10)) (= (select (lambda ((i Int)) (+ i k)) 1) 11))")]
    [TestCase("(select (lambda ((i Int)) (or (= i 4) (= i 6))) 6)")]
    [TestCase("(= (select ((as const (Array Real Int)) 0) 1) 0)")]
    [TestCase("(= (select (store ((as const (Array Real Int)) 0) 2 9) 2.0) 9)")]
    [TestCase("(= (select (store ((as const (Array String Int)) 0) \"k\" 9) (str.++ \"\" \"k\")) 9)")]
    [TestCase("(= (select (store ((as const (Array (_ BitVec 4) Bool)) false) #xa true) (bvadd #x9 #x1)) true)")]
    public void SelectReadsWhatWasStoredOverTheBaseTest(string assertion)
    {
        Assert.That(SmtCheck.Holds(string.Format(assertion, ZEROS)).Class, Is.EqualTo(SmtValidationClass.Validated), assertion);
    }

    #endregion

    #region Equality Tests

    [TestCase("(= {0} {0})", true)]
    [TestCase("(= (store {0} 1 5) (store {0} 1 5))", true)]
    [TestCase("(= (store (store {0} 1 5) 2 6) (store (store {0} 2 6) 1 5))", true)]
    [TestCase("(= (store {0} 1 0) {0})", true)]
    [TestCase("(= (store (store {0} 1 5) 1 0) {0})", true)]
    [TestCase("(= (store {0} 1 5) {0})", false)]
    [TestCase("(= (store {0} 1 5) (store {0} 1 6))", false)]
    [TestCase("(= (store {0} 1 5) (store {0} 2 5))", false)]
    [TestCase("(= {0} ((as const (Array Int Int)) 1))", false)]
    [TestCase("(= (store {0} 1 5) (store ((as const (Array Int Int)) 1) 1 5))", false)]
    [TestCase("(= (lambda ((i Int)) (ite (= i 1) 5 0)) (store {0} 1 5))", true)]
    [TestCase("(= (lambda ((i Int)) (ite (= i 1) 5 (ite (= i 1) 6 0))) (store {0} 1 5))", true)]
    [TestCase("(= (lambda ((i Int)) (ite (= 1 i) 5 0)) (store {0} 1 6))", false)]
    [TestCase("(distinct {0} (store {0} 1 5) (store {0} 2 5))", true)]
    [TestCase("(distinct {0} (store {0} 1 5) (store {0} 1 0))", false)]
    public void FiniteMapsAreComparedIndexByIndexTest(string assertion, bool expected)
    {
        var validation = SmtCheck.Holds(string.Format(assertion, ZEROS));

        Assert.That(validation.Class, Is.EqualTo(expected ? SmtValidationClass.Validated : SmtValidationClass.Failed), assertion);
    }

    [Test]
    public void ArraysOverASortTheyExhaustAreEqualWhateverTheirDefaultsTest()
    {
        // Both arrays store every index there is: the defaults are never read.
        const string bits = "(= (store (store ((as const (Array (_ BitVec 1) Int)) 0) #b0 5) #b1 6) (store (store ((as const (Array (_ BitVec 1) Int)) 9) #b1 6) #b0 5))";
        const string truths = "(= (store (store ((as const (Array Bool Int)) 0) true 5) false 6) (store (store ((as const (Array Bool Int)) 9) false 6) true 5))";
        const string half = "(= (store ((as const (Array Bool Int)) 0) true 5) (store ((as const (Array Bool Int)) 9) true 5))";

        Assert.That(SmtCheck.Holds(bits).Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Holds(truths).Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Holds(half).Class, Is.EqualTo(SmtValidationClass.Failed));
    }

    [Test]
    public void ArraysOfArraysAreComparedThroughTest()
    {
        const string outer = "((as const (Array Int (Array Int Int))) {0})";
        var same = string.Format($"(= (store {outer} 1 (store {{0}} 2 3)) (store {outer} 1 (store (store {{0}} 2 3) 4 0)))", ZEROS);
        var different = string.Format($"(= (store {outer} 1 (store {{0}} 2 3)) (store {outer} 1 (store {{0}} 2 4)))", ZEROS);

        Assert.That(SmtCheck.Holds(same).Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Holds(different).Class, Is.EqualTo(SmtValidationClass.Failed));
    }

    [Test]
    public void ArrayIsNoIndexOfAnArrayTest()
    {
        // Two arrays can be the same array without being written the same, so a map keyed
        // by arrays would miss what was stored.
        var validation = SmtCheck.Holds($"(= (select ((as const (Array (Array Int Int) Int)) 0) {ZEROS}) 0)");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNSUPPORTED_SORT));
    }

    #endregion

    #region Model Tests

    [Test]
    public void FunctionOfTheModelBehindAnArrayIsAFiniteMapWhereItsBodyAllowsTest()
    {
        const string problem = "(declare-const a (Array Int Int))(declare-const b (Array Int Int))(assert (= a (store (store ((as const (Array Int Int)) 1) 3 30) 2 20)))(assert (= b a))(assert (= (select b 99) 1))";
        const string model = "((define-fun a () (Array Int Int) (_ as-array k!0)) (define-fun b () (Array Int Int) (_ as-array k!0)) (define-fun k!0 ((x!0 Int)) Int (ite (= x!0 2) 20 (ite (= x!0 3) 30 1))))";

        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void FunctionOfTheModelBehindAnArrayIsReadWhereItsBodyIsNoMapTest()
    {
        const string problem = "(declare-const a (Array Int Int))(assert (= (select a 4) 8))(assert (= (select (store a 4 1) 4) 1))(assert (= (select (store a 4 1) 5) 10))";
        const string model = "((define-fun a () (Array Int Int) (_ as-array k!0)) (define-fun k!0 ((x!0 Int)) Int (* 2 x!0)))";

        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void DeclaredFunctionTakenAsAnArrayIsItsInterpretationTest()
    {
        const string problem = "(declare-fun f (Int) Int)(declare-const a (Array Int Int))(assert (= (select a 1) (f 1)))(assert (= (f 1) 3))";
        const string model = "((define-fun f ((x!0 Int)) Int (ite (= x!0 1) 3 0)) (define-fun a () (Array Int Int) (_ as-array f)))";

        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void ArrayOfACompletedFunctionRestsOnTheCompletedSymbolTest()
    {
        const string problem = "(declare-fun f (Int) Int)(declare-const a (Array Int Int))(assert (= (select a 1) 0))(assert (= (select a 2) 3))";
        const string model = "((define-fun a () (Array Int Int) (_ as-array f)))";

        var validation = SmtCheck.Validate(problem, model);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.COMPLETED_SYMBOL));
    }

    #endregion
}
