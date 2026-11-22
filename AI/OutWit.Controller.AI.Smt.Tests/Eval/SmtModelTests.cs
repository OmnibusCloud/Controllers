using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtModelTests
{
    #region Constants

    private const string PRINTED = """
        (
          ;; universe for U:
          ;;   U!val!0 U!val!1
          ;; -----------
          ;; definitions for universe elements:
          (declare-fun U!val!0 () U)
          (declare-fun U!val!1 () U)
          ;; cardinality constraint:
          (forall ((x U)) (or (= x U!val!0) (= x U!val!1)))
          ;; -----------
          (define-fun u () U
            U!val!1)
          (define-fun f ((x!0 U)) Int
            (ite (= x!0 U!val!1) 7 0))
        )
        """;

    #endregion

    #region Read Tests

    [Test]
    public void ReadTakesTheAnswerOfGetModelAsASolverPrintsItTest()
    {
        var model = SmtModel.Read(PRINTED, null, SmtReaderLimits.Problem);

        Assert.That(model.IsReadable, Is.True);
        Assert.That(model.Definitions, Is.EqualTo(2));
        Assert.That(model.UniverseElements, Is.EqualTo(2));
        Assert.That(model.Values, Is.EqualTo(0));
    }

    [Test]
    public void ReadTakesTheOlderWrappingOfAModelTest()
    {
        var model = SmtModel.Read("(model (define-fun x () Int 1) (define-fun y () Int (- 2)))", null, SmtReaderLimits.Problem);

        Assert.That(model.IsReadable, Is.True);
        Assert.That(model.Definitions, Is.EqualTo(2));
        Assert.That(SmtCheck.Validate("(declare-const x Int)(declare-const y Int)(assert (= (+ x y) (- 1)))", "(model (define-fun x () Int 1) (define-fun y () Int (- 2)))").Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [Test]
    public void ReadTakesTheAnswerOfGetValueTest()
    {
        var model = SmtModel.Read(null, "((x 1)\n (|a b| (- 2))\n ((f 1) 3)\n ((select a 0) 4))", SmtReaderLimits.Problem);

        // A pair whose term is no constant defines no symbol.
        Assert.That(model.IsReadable, Is.True);
        Assert.That(model.Values, Is.EqualTo(2));
        Assert.That(model.Definitions, Is.EqualTo(0));
    }

    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase("  \n\t ", null)]
    [TestCase("()", "()")]
    [TestCase("(\n)", null)]
    [TestCase("(model)", null)]
    public void ReadTakesNoAnswerAndAnEmptyAnswerAsAModelThatDefinesNothingTest(string? text, string? values)
    {
        var model = SmtModel.Read(text, values, SmtReaderLimits.Problem);

        Assert.That(model.IsReadable, Is.True);
        Assert.That(model.Definitions + model.Values + model.UniverseElements, Is.EqualTo(0));
        Assert.That(SmtCheck.Validate("(assert (= 1 1))", text, values).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    [TestCase("sat")]
    [TestCase("(error \"line 3: unknown constant\")")]
    [TestCase("(define-fun x () Int 1)")]
    [TestCase("((define-fun x () Int 1)) ((define-fun y () Int 1))")]
    [TestCase("((define-fun x () Int 1) (define-fun x () Int 1))")]
    [TestCase("((declare-fun e () U) (define-fun e () U e))")]
    [TestCase("((declare-fun e () U) (declare-fun e () U))")]
    [TestCase("((define-fun x () Int))")]
    [TestCase("((define-fun 5 () Int 1))")]
    [TestCase("((define-fun x (y) Int 1))")]
    [TestCase("((define-fun x ((y Int) (y Int)) Int 1))")]
    [TestCase("((define-fun x ((true Bool)) Int 1))")]
    [TestCase("((define-fun + ((a Int) (b Int)) Int 0))")]
    [TestCase("((define-fun-rec x () Int 1))")]
    [TestCase("((declare-fun f (Int) Int))")]
    [TestCase("((|define-fun| x () Int 1))")]
    [TestCase("((assert true))")]
    [TestCase("(x)")]
    [TestCase("((define-fun x () Int 1)")]
    [TestCase("((define-fun x () Int 1)))")]
    [TestCase("((define-fun x () Int 007))")]
    public void ReadFindsATextThatIsNoModelUnreadableTest(string text)
    {
        var model = SmtModel.Read(text, null, SmtReaderLimits.Problem);

        Assert.That(model.IsReadable, Is.False);
        Assert.That(model.Definitions + model.Values + model.UniverseElements, Is.EqualTo(0));
    }

    [TestCase("x 1")]
    [TestCase("(x 1)")]
    [TestCase("((x 1) (x 2))")]
    [TestCase("((x))")]
    [TestCase("((x 1 2))")]
    [TestCase("((x 1)) ((y 2))")]
    [TestCase("((x \"open))")]
    public void ReadFindsATextThatIsNoAnswerOfGetValueUnreadableTest(string values)
    {
        Assert.That(SmtModel.Read(null, values, SmtReaderLimits.Problem).IsReadable, Is.False);
        Assert.That(SmtModel.Read("((define-fun y () Int 1))", values, SmtReaderLimits.Problem).IsReadable, Is.False);
    }

    [Test]
    public void ReadFindsATextPastALimitUnreadableTest()
    {
        const string text = "((define-fun x () Int (+ 1 (+ 2 (+ 3 4)))))";

        Assert.That(SmtModel.Read(text, null, new SmtReaderLimits { MaxCharacters = text.Length }).IsReadable, Is.True);
        Assert.That(SmtModel.Read(text, null, new SmtReaderLimits { MaxCharacters = text.Length - 1 }).IsReadable, Is.False);
        Assert.That(SmtModel.Read(text, null, new SmtReaderLimits { MaxDepth = 4 }).IsReadable, Is.False);
        Assert.That(SmtModel.Read(text, null, new SmtReaderLimits { MaxTokens = 10 }).IsReadable, Is.False);
        Assert.That(SmtModel.Read(null, text, new SmtReaderLimits { MaxTokens = 10 }).IsReadable, Is.False);
    }

    [Test]
    public void ReadNeverThrowsForATextTest()
    {
        var random = new Random(20261122);
        const string alphabet = "()|\"; \n\\_!x1#b.-:\0\u00e9";
        for (var round = 0; round < 3000; round++)
        {
            var text = new string(Enumerable.Range(0, random.Next(40)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

            Assert.DoesNotThrow(() => SmtModel.Read(text, text, SmtReaderLimits.Problem));
        }
    }

    [Test]
    public void ReadAsksForLimitsTest()
    {
        Assert.Throws<ArgumentNullException>(() => SmtModel.Read("()", null, null!));
    }

    #endregion

    #region Use Tests

    [Test]
    public void UnreadableModelIsAPartialAnswerTest()
    {
        var validation = SmtCheck.Validate("(declare-const x Int)(assert (= x 1))(assert (= x 2))", "sat");

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.UNREADABLE_MODEL));
        Assert.That(validation.Assertions, Is.EqualTo(2));
        Assert.That(validation.True, Is.EqualTo(0));
        Assert.That(validation.Steps, Is.EqualTo(0));
    }

    [Test]
    public void ModelIsCheckedAgainstAnyNumberOfProblemsTest()
    {
        // A model is only what the solver printed: what its names mean is the problem's to say.
        var model = SmtCheck.Model("((define-fun x () Int 5) (define-fun flag () Bool true))");

        var first = new SmtModelValidator(SmtCheck.Commands("(declare-const x Int)(assert (> x 4))")).Validate(model, SmtEvaluationBudget.Default);
        var second = new SmtModelValidator(SmtCheck.Commands("(declare-const flag Bool)(declare-const x Int)(assert (=> flag (< x 5)))")).Validate(model, SmtEvaluationBudget.Default);

        Assert.That(first.Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(first.Defined, Is.EqualTo(1));
        Assert.That(second.Class, Is.EqualTo(SmtValidationClass.Failed));
        Assert.That(second.Defined, Is.EqualTo(2));
    }

    [Test]
    public void ValidatorAsksForItsArgumentsTest()
    {
        var validator = new SmtModelValidator([]);

        Assert.Throws<ArgumentNullException>(() => _ = new SmtModelValidator((IReadOnlyList<SmtExpression>)null!));
        Assert.Throws<ArgumentNullException>(() => _ = new SmtModelValidator((SmtProblem)null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, SmtEvaluationBudget.Default));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(SmtCheck.Model(null), null!));
        Assert.That(validator.Validate(SmtCheck.Model(null), SmtEvaluationBudget.Default).Class, Is.EqualTo(SmtValidationClass.Validated));
    }

    #endregion
}
