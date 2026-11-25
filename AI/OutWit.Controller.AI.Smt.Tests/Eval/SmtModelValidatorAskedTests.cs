using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The answer of <c>get-value</c> held to the terms that were asked, where the caller knows
/// them: as many pairs as terms, each pair's term written as the term asked at its place. An
/// answer that is not the answer of what was asked is used for nothing, and never validated.
/// </summary>
[TestFixture]
public class SmtModelValidatorAskedTests
{
    #region Constants

    private const string SUM = "(declare-const x Int)(declare-const y Int)(declare-fun f (Int) Int)(assert (= (+ x y) 10))";
    private const string SUM_MODEL = "((define-fun x () Int 4) (define-fun y () Int 6) (define-fun f ((x!0 Int)) Int (ite (= x!0 1) 7 0)))";

    #endregion

    #region Asked Tests

    [Test]
    public void AnswerOfTheTermsAskedIsHeldToThemAndToTheModelTest()
    {
        var right = SmtCheck.ValidateAsked(SUM, SUM_MODEL, "((x 4) ((+ x y) 10) ((f 1) 7))", "x", "(+ x y)", "(f 1)");
        var wrong = SmtCheck.ValidateAsked(SUM, SUM_MODEL, "((x 4) ((+ x y) 10) ((f 1) 8))", "x", "(+ x y)", "(f 1)");

        Assert.That(right.Class, Is.EqualTo(SmtValidationClass.Validated), right.ToString());
        Assert.That((wrong.Class, wrong.Reason, wrong.Detail), Is.EqualTo((SmtValidationClass.Failed, SmtValidationReasons.VALUE_MISMATCH, "3")));
    }

    [TestCase("((x 4))", "fewer pairs than terms")]
    [TestCase("((x 4) ((+ x y) 10) ((f 1) 7) (y 6))", "more pairs than terms")]
    [TestCase("((x 4) ((+ x y) 10) ((f 2) 0))", "the value of another term, right for that term")]
    [TestCase("((x 4) ((+ y x) 10) ((f 1) 7))", "a term written another way")]
    [TestCase("(((+ x y) 10) (x 4) ((f 1) 7))", "the terms in another order")]
    [TestCase("((x 4) ((+ x y) 10) ((f 1) 7)", "a list left open")]
    [TestCase(null, "no answer at all")]
    public void AnswerThatIsNotTheAnswerOfTheTermsAskedIsNotTakenTest(string? values, string what)
    {
        var validation = SmtCheck.ValidateAsked(SUM, SUM_MODEL, values, "x", "(+ x y)", "(f 1)");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), what);
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNREADABLE_VALUES), what);
        Assert.That(validation.True, Is.EqualTo(validation.Assertions), "the model is still checked as the model");
    }

    [Test]
    public void TermsAreComparedAsTheyAreWrittenTest()
    {
        // A symbol is the same symbol with bars or without; a literal is the one that was written.
        const string problem = "(declare-const x Real)(assert (= x 1.5))";
        const string model = "((define-fun x () Real (/ 3.0 2.0)))";

        var bars = SmtCheck.ValidateAsked(problem, model, "((x 1.5) ((+ x 1.50) 3.0))", "|x|", "(+ |x| 1.50)");
        var literal = SmtCheck.ValidateAsked(problem, model, "((x 1.5) ((+ x 1.5) 3.0))", "x", "(+ x 1.50)");

        Assert.That(bars.Class, Is.EqualTo(SmtValidationClass.Validated), bars.ToString());
        Assert.That((literal.Class, literal.Reason), Is.EqualTo((SmtValidationClass.Partial, SmtValidationReasons.UNREADABLE_VALUES)));
    }

    [Test]
    public void AnswerThatIsNotTakenCompletesNothingTest()
    {
        // y is left out by the model and is not among the terms asked: its value is not taken
        // from an answer that is not the answer of what was asked.
        const string problem = "(declare-const x Int)(declare-const y Int)(assert (> x 0))(assert (> y 5))";
        const string model = "((define-fun x () Int 1))";

        var taken = SmtCheck.ValidateAsked(problem, model, "((y 7))", "y");
        var other = SmtCheck.ValidateAsked(problem, model, "((y 7))", "x");

        Assert.Multiple(() =>
        {
            Assert.That(taken.Class, Is.EqualTo(SmtValidationClass.Validated), taken.ToString());
            Assert.That(taken.Defined, Is.EqualTo(2));
            Assert.That((other.Class, other.Reason), Is.EqualTo((SmtValidationClass.Partial, SmtValidationReasons.UNREADABLE_VALUES)));
            Assert.That(other.True, Is.EqualTo(1), "y takes its sort's default, not the 7");
            Assert.That(other.Defined, Is.EqualTo(1));
        });
    }

    [Test]
    public void NoTermAskedGoesWithNoValueGivenTest()
    {
        var neither = SmtCheck.ValidateAsked(SUM, SUM_MODEL, null);
        var blank = SmtCheck.ValidateAsked(SUM, SUM_MODEL, " \n");
        var given = SmtCheck.ValidateAsked(SUM, SUM_MODEL, "((x 4))");
        var empty = SmtCheck.ValidateAsked(SUM, SUM_MODEL, "()", "x");

        Assert.Multiple(() =>
        {
            Assert.That(neither.Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That(blank.Class, Is.EqualTo(SmtValidationClass.Validated));
            Assert.That((given.Class, given.Reason), Is.EqualTo((SmtValidationClass.Partial, SmtValidationReasons.UNREADABLE_VALUES)), "values nobody asked for");
            Assert.That((empty.Class, empty.Reason), Is.EqualTo((SmtValidationClass.Partial, SmtValidationReasons.UNREADABLE_VALUES)), "a list without the pair that was asked for");
        });
    }

    [Test]
    public void ValidateAsksForTheTermsTest()
    {
        var validator = new SmtModelValidator(SmtCheck.Commands(SUM));
        var model = SmtCheck.Model(SUM_MODEL);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(model, null!, SmtEvaluationBudget.Default));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, [], SmtEvaluationBudget.Default));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(model, [], null!));
        Assert.Throws<ArgumentException>(() => validator.Validate(model, [null!], SmtEvaluationBudget.Default));
        Assert.That(validator.Validate(model, [], SmtEvaluationBudget.Default).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion
}
