using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// What the evaluator reads from end to end before it makes a value of it, or finds that there
/// is none: the variables of a pattern, the indices of an identifier, the characters of a
/// string turned into a number, the digits of a binary literal. Each once cost a step, or
/// nothing, whatever its length; each now pays by its length before it is read. The shapes
/// real problems are written in cost what they did, but for a pattern, which pays for its
/// variables.
/// </summary>
[TestFixture]
public class SmtModelValidatorReadPriceTests
{
    #region Constants

    /// <summary>The fields of the constructor whose pattern is read over and over: 650 kilobytes of problem.</summary>
    private const int PATTERN_VARIABLES = 50_000;

    private const int PATTERN_DEPTH = 11;

    /// <summary>Twenty times the 0.3 s the hundred readings the budget pays for take, and a little.</summary>
    private const double PATTERN_BOUND_SECONDS = 7;

    private const int MANY_INDICES = 5_000;

    /// <summary>The steps of an extract and of an extension, as they were before long lists of indices were paid for.</summary>
    private const int INDEXED_STEPS = 9;

    /// <summary>The characters of the string that is no number: just under the ceiling of a string.</summary>
    private const int NO_NUMBER_CHARACTERS = 1_000_000;

    private const int NO_NUMBER_DEPTH = 15;

    /// <summary>Twenty times the 0.32 s the 360 readings the budget pays for take, and a little.</summary>
    private const double NO_NUMBER_BOUND_SECONDS = 7;

    /// <summary>The steps of turning three digits into a number and comparing it, as they were before the look was paid for.</summary>
    private const int NUMBER_STEPS = 13;

    /// <summary>The steps of finding that two characters are no number, as they were.</summary>
    private const int NO_NUMBER_STEPS = 12;

    #endregion

    #region Pattern Tests

    [Test]
    public void PatternOfManyVariablesPaysForEveryVariableTest()
    {
        // The pattern of a constructor of a thousand fields, over a value of the other
        // constructor: its variables are checked and told apart all the same.
        var one = SmtCheck.Validate(Record(1), Matching(1, 0));
        var thousand = SmtCheck.Validate(Record(1000), Matching(1000, 0));

        Assert.That(one.Class, Is.EqualTo(SmtValidationClass.Validated), one.ToString());
        Assert.That(thousand.Class, Is.EqualTo(SmtValidationClass.Validated), thousand.ToString());
        Assert.That(thousand.Steps - one.Steps, Is.EqualTo(999 * SmtTerms.VARIABLE_STEPS));
    }

    [Test]
    public void PatternOfManyVariablesReadAtEveryCallEndsAtTheStepBudgetTest()
    {
        // A constructor of 50,000 fields, and a model's own function that matches against its
        // pattern at each of 4,095 calls: 2.2 ms a reading. Read for the one step of its case,
        // the pattern was read every time and the model validated, in 9 s. Paid for variable by
        // variable, the budget ends the evaluation at the hundredth reading, in 0.3 s.
        var (validation, elapsed) = SmtTimed.Validate(Record(PATTERN_VARIABLES), Matching(PATTERN_VARIABLES, PATTERN_DEPTH));

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(PATTERN_BOUND_SECONDS), $"the budget was used up in {elapsed.TotalSeconds:F2} s");
    }

    [Test]
    public void PatternOfAnOrdinaryConstructorPaysForItsVariablesTest()
    {
        // (cons h t): its two variables are paid for at every reading, beside the one step of its case.
        const string lists = "(declare-datatypes ((L 0)) (((nil) (cons (head Int) (tail L)))))";

        var symbol = SmtCheck.Holds("(= (match (cons 7 nil) ((nil 0) (l 1))) 1)", lists);
        var pattern = SmtCheck.Holds("(= (match (cons 7 nil) ((nil 0) ((cons h t) 1))) 1)", lists);

        Assert.That(symbol.Class, Is.EqualTo(SmtValidationClass.Validated), symbol.ToString());
        Assert.That(pattern.Class, Is.EqualTo(SmtValidationClass.Validated), pattern.ToString());
        Assert.That(pattern.Steps - symbol.Steps, Is.EqualTo(2 * SmtTerms.VARIABLE_STEPS));
    }

    #endregion

    #region Index Tests

    [Test]
    public void IndicesBeyondWhatAnOperatorTakesPayAStepEachTest()
    {
        // No operator takes three indices: the term fits no reading, but every index of it was
        // read first to see that it is a numeral.
        var three = SmtCheck.Holds("(= ((_ extract 0 0 0) #b1) #b1)");
        var many = SmtCheck.Holds($"(= ((_ extract{string.Concat(Enumerable.Repeat(" 0", MANY_INDICES))}) #b1) #b1)");

        Assert.That(three.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
        Assert.That(many.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
        Assert.That(many.Steps - three.Steps, Is.EqualTo(MANY_INDICES - 3));
    }

    [TestCase("(= ((_ extract 7 4) #xab) #xa)")]
    [TestCase("(= ((_ zero_extend 4) #xa) #x0a)")]
    public void IndicesAnOperatorTakesCostWhatTheyDidTest(string assertion)
    {
        var validation = SmtCheck.Holds(assertion);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
        Assert.That(validation.Steps, Is.EqualTo(INDEXED_STEPS));
    }

    #endregion

    #region String Tests

    [Test]
    public void StringThatIsNoNumberReadAtEveryCallEndsAtTheStepBudgetTest()
    {
        // (str.to_int s) of a string of a million digits and a letter, held in a parameter and
        // read at each of 32,767 calls. Every character was looked at and copied before the
        // string was found to be no number, and nothing was paid: 0.8 ms a reading, and the
        // model validated in 27 s. Paid for like the digits of a numeral, a step for every 18
        // characters, before the look, the budget ends the evaluation at the 360th reading, in
        // 0.32 s.
        var (validation, elapsed) = SmtTimed.Validate("(declare-const x Int)(assert (= x 0))", NoNumbers(NO_NUMBER_CHARACTERS, NO_NUMBER_DEPTH));

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(NO_NUMBER_BOUND_SECONDS), $"the budget was used up in {elapsed.TotalSeconds:F2} s");
    }

    [Test]
    public void StringTurnedIntoANumberPaysForEveryCharacterLookedAtTest()
    {
        // Two strings that are no numbers, of 17 and of 1,800 characters: the literal pays a
        // step a character, as it did, and the look a step for every 18.
        var below = SmtCheck.Holds($"(= (str.to_int \"{new string('a', 17)}\") (- 1))");
        var longer = SmtCheck.Holds($"(= (str.to_int \"{new string('a', 1800)}\") (- 1))");

        Assert.That(below.Class, Is.EqualTo(SmtValidationClass.Validated), below.ToString());
        Assert.That(longer.Class, Is.EqualTo(SmtValidationClass.Validated), longer.ToString());
        Assert.That(longer.Steps - below.Steps, Is.EqualTo(1800 - 17 + 1800 / 18));
    }

    [TestCase("(= (str.to_int \"007\") 7)", NUMBER_STEPS)]
    [TestCase("(= (str.to_int \"12345678901234567\") 12345678901234567)", NUMBER_STEPS + 14)]
    [TestCase("(= (str.to_int \"1a\") (- 1))", NO_NUMBER_STEPS)]
    public void StringOfFewCharactersTurnedIntoANumberCostsWhatItDidTest(string assertion, int steps)
    {
        // The second string is fourteen characters longer than the first, a step each.
        var validation = SmtCheck.Holds(assertion);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Validated), validation.ToString());
        Assert.That(validation.Steps, Is.EqualTo(steps));
    }

    #endregion

    #region Literal Tests

    [Test]
    public void BinaryLiteralPaysForItsDigitsTest()
    {
        // Two literals of 6,400 digits compared: each pays eight steps for every 64 digits
        // (it paid one), and the comparison a step for every 64 bits, as it did. Below 64
        // digits a literal pays the one step it always did.
        var narrow = SmtCheck.Holds("(= #b1 #b1)");
        var below = SmtCheck.Holds($"(= #b{new string('1', 63)} #b{new string('1', 63)})");
        var wide = SmtCheck.Holds($"(= #b{new string('1', 6400)} #b{new string('1', 6400)})");

        Assert.That(wide.Class, Is.EqualTo(SmtValidationClass.Validated), wide.ToString());
        Assert.That(below.Steps, Is.EqualTo(narrow.Steps));
        Assert.That(wide.Steps - narrow.Steps, Is.EqualTo(2 * 8 * 100 + 100));
    }

    #endregion

    #region Tools

    /// <summary>
    /// The problem with a datatype of two constructors, one of so many fields and one of none,
    /// and a constant the model is to give the value 0.
    /// </summary>
    private static string Record(int fields)
    {
        var problem = new StringBuilder("(declare-datatypes ((R 0)) (((mk");
        for (var index = 0; index < fields; index++)
            problem.Append(" (f").Append(index).Append(" Int)");

        return problem.Append(") (none))))(declare-const x Int)(assert (= x 0))").ToString();
    }

    /// <summary>
    /// The model whose own function calls itself twice a level, down from a depth, and at every
    /// call matches the value of the constructor without fields against the pattern of the
    /// other one.
    /// </summary>
    private static string Matching(int fields, int depth)
    {
        var pattern = new StringBuilder("(mk");
        for (var index = 0; index < fields; index++)
            pattern.Append(" v").Append(index);

        pattern.Append(')');
        return $"((define-fun m!0 ((n Int)) Int (+ (match none (({pattern} 1) (none 0))) (ite (<= n 0) 0 (+ (m!0 (- n 1)) (m!0 (- n 1)))))) (define-fun x () Int (m!0 {depth})))";
    }

    /// <summary>
    /// The model whose own function calls itself twice a level, down from a depth, and at every
    /// call that calls on turns into a number a string of so many digits with a letter at its end.
    /// </summary>
    private static string NoNumbers(int characters, int depth)
    {
        return $"((define-fun t!0 ((s String) (n Int)) Int (ite (<= n 0) 0 (+ (str.to_int s) 1 (t!0 s (- n 1)) (t!0 s (- n 1))))) (define-fun x () Int (t!0 \"{new string('9', characters - 1)}a\" {depth})))";
    }

    #endregion
}
