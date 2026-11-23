using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The elements of an uninterpreted sort, listed by the model or only named by it
/// (<c>A!val!1</c> as the value of a function, with no universe of <c>A</c> printed). Each case
/// is run twice in one test, the universe listed and not listed, and must come out the same: an
/// element is an element of its sort, the same element exactly when its name is the same. Each
/// case has a mutant, the model with one element changed for another, that the evaluator must
/// call failed.
/// </summary>
[TestFixture]
public class SmtModelValidatorElementTests
{
    #region Constants

    private const string UNIVERSE = "(declare-fun A!val!0 () A) (declare-fun A!val!1 () A) (forall ((x A)) (or (= x A!val!0) (= x A!val!1)))";

    #endregion

    #region Listed And Unlisted Tests

    [TestCaseSource(nameof(Cases))]
    public void AnElementListedOrNotIsReadTheSameWayTest(string name, string problem, string model, string mutant)
    {
        Assert.Multiple(() =>
        {
            foreach (var listed in new[] { true, false })
            {
                var universe = listed ? UNIVERSE + " " : "";
                var where = $"{name}, {(listed ? "listed" : "not listed")}";

                var validation = SmtCheck.Validate(problem, $"({universe}{model})");
                var mutated = SmtCheck.Validate(problem, $"({universe}{mutant})");

                Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), $"{where}: {validation.Reason} {validation.Detail}");
                Assert.That(mutated.Class, Is.EqualTo(SmtValidationClass.Failed), $"{where}, the mutant: {mutated.Reason} {mutated.Detail}");
                Assert.That(mutated.Reason, Is.EqualTo(SmtValidationReasons.ASSERTION_FALSE), where);
            }
        });
    }

    [Test]
    public void AnUnlistedElementDiffersFromEveryListedOneTest()
    {
        const string problem = "(declare-sort A 0)(declare-const a A)(declare-const b A)(assert (distinct a b))";

        // The universe lists A!val!0 only; A!val!1 is named and not listed.
        Assert.That(SmtCheck.Validate(problem, "((declare-fun A!val!0 () A) (define-fun a () A A!val!0) (define-fun b () A A!val!1))").Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Validate(problem, "((declare-fun A!val!0 () A) (define-fun a () A A!val!1) (define-fun b () A A!val!1))").Class, Is.EqualTo(SmtValidationClass.Failed));
    }

    [Test]
    public void AnUnlistedElementIsReadInTheAnswerOfGetValueTest()
    {
        const string problem = "(declare-sort A 0)(declare-const a A)(declare-const b A)(assert (distinct a b))";

        Assert.That(SmtCheck.ValidateValues(problem, "((a A!val!0) (b A!val!1))").Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.ValidateValues(problem, "((a A!val!3) (b A!val!3))").Class, Is.EqualTo(SmtValidationClass.Failed));
    }

    #endregion

    #region Unknown Tests

    [TestCase("(declare-sort A 0)(declare-const a A)(assert (= a a))", "((define-fun a () A B!val!0))", TestName = "{m}(a sort the problem does not declare)")]
    [TestCase("(declare-sort P 1)(declare-const a (P Int))(assert (= a a))", "((define-fun a () (P Int) P!val!0))", TestName = "{m}(a sort with a parameter)")]
    [TestCase("(declare-sort A 0)(declare-const a A)(assert (= a a))", "((define-fun a () A A!val!x))", TestName = "{m}(no number after the mark)")]
    [TestCase("(declare-sort A 0)(declare-const a A)(assert (= a a))", "((define-fun a () A A!val!))", TestName = "{m}(nothing after the mark)")]
    [TestCase("(declare-sort A 0)(define-sort B () A)(declare-const a A)(assert (= a a))", "((define-fun a () A B!val!0))", TestName = "{m}(an alias of a sort)")]
    [TestCase("(declare-sort A 0)(declare-const a A)(assert (= a A!val!0))", "((define-fun a () A A!val!0))", TestName = "{m}(a term of the problem, which never sees the model's names)")]
    public void ANameThatIsNoElementStaysUnknownTest(string problem, string model)
    {
        var validation = SmtCheck.Validate(problem, model);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNKNOWN_SYMBOL));
    }

    [Test]
    public void ANameSomebodyDeclaresIsTheirsTest()
    {
        // The model defines A!val!3 itself: its value is A!val!0, and no element is named A!val!3.
        const string modelsOwn = "(declare-sort A 0)(declare-const a A)(declare-const b A)(assert (= a b))";
        Assert.That(SmtCheck.Validate(modelsOwn, "((define-fun A!val!3 () A A!val!0) (define-fun a () A A!val!3) (define-fun b () A A!val!0))").Class, Is.EqualTo(SmtValidationClass.Validated));

        // The problem declares A!val!5: in the model's text too the name is the problem's constant.
        const string problemsOwn = "(declare-sort A 0)(declare-const A!val!5 A)(declare-const a A)(assert (= a A!val!5))";
        Assert.That(SmtCheck.Validate(problemsOwn, "((define-fun A!val!5 () A A!val!0) (define-fun a () A A!val!5))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion

    #region Tools

    private static IEnumerable<TestCaseData> Cases()
    {
        var cases = new (string Name, string Problem, string Model, string Mutant)[]
        {
            ("equal",
                "(declare-sort A 0)(declare-const a A)(declare-const b A)(assert (= a b))",
                "(define-fun a () A A!val!0) (define-fun b () A A!val!0)",
                "(define-fun a () A A!val!0) (define-fun b () A A!val!1)"),
            ("distinct",
                "(declare-sort A 0)(declare-const a A)(declare-const b A)(assert (distinct a b))",
                "(define-fun a () A A!val!0) (define-fun b () A A!val!1)",
                "(define-fun a () A A!val!0) (define-fun b () A A!val!0)"),
            ("a function's argument and result",
                "(declare-sort A 0)(declare-fun f (A) A)(declare-const a A)(assert (distinct (f a) a))(assert (= (f (f a)) a))",
                "(define-fun a () A A!val!0) (define-fun f ((x!0 A)) A (ite (= x!0 A!val!0) A!val!1 A!val!0))",
                "(define-fun a () A A!val!0) (define-fun f ((x!0 A)) A (ite (= x!0 A!val!0) A!val!0 A!val!0))"),
            ("an array's index",
                "(declare-sort A 0)(declare-const m (Array A Int))(declare-const a A)(declare-const b A)(assert (= (select m a) 5))(assert (= (select m b) 0))",
                "(define-fun m () (Array A Int) (store ((as const (Array A Int)) 0) A!val!1 5)) (define-fun a () A A!val!1) (define-fun b () A A!val!0)",
                "(define-fun m () (Array A Int) (store ((as const (Array A Int)) 0) A!val!1 5)) (define-fun a () A A!val!1) (define-fun b () A A!val!1)"),
            ("an array's element",
                "(declare-sort A 0)(declare-const m (Array Int A))(declare-const a A)(assert (= (select m 7) a))(assert (distinct (select m 8) a))",
                "(define-fun m () (Array Int A) (store ((as const (Array Int A)) A!val!0) 7 A!val!1)) (define-fun a () A A!val!1)",
                "(define-fun m () (Array Int A) (store ((as const (Array Int A)) A!val!1) 7 A!val!1)) (define-fun a () A A!val!1)"),
            ("a datatype's field",
                "(declare-sort A 0)(declare-datatype Box ((box (content A))))(declare-const p Box)(declare-const a A)(assert (= (content p) a))(assert (= p (box a)))",
                "(define-fun p () Box (box A!val!0)) (define-fun a () A A!val!0)",
                "(define-fun p () Box (box A!val!1)) (define-fun a () A A!val!0)")
        };

        foreach (var (name, problem, model, mutant) in cases)
            yield return new TestCaseData(name, problem, model, mutant).SetName($"{{m}}({name})");
    }

    #endregion
}
