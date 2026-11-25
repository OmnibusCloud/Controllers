using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// Models made to cost more than they pay: each does work in a place where the step budget once
/// saw none, and held a validation for minutes inside its budget. The price of every such place
/// is now its work, and each test holds one of them to that: by the count where the count shows
/// it (the class, the reason, the steps), and by the clock where only the clock can.
/// <para>
/// A bound on the clock is twenty times what the fixed code took on the machine it was written
/// on, and never less than <see cref="LEAST_BOUND_SECONDS"/>; the size of each model is one at
/// which the code before the fix took at least ten times the bound there. The measured times
/// stand beside each bound.
/// </para>
/// </summary>
[TestFixture]
public class SmtModelValidatorPriceTests
{
    #region Constants

    /// <summary>No bound on the clock is tighter than this: a busy build machine stalls for a second now and then.</summary>
    private const double LEAST_BOUND_SECONDS = 2;

    /// <summary>The digits of each of the two numerals a large rational is made of: 100,000 bits a numeral.</summary>
    private const int RATIONAL_DIGITS = 30_000;

    /// <summary>The steps of one link of a chain of <c>let</c> that negates: the let, the application, the name.</summary>
    private const int NEGATION_STEPS = 3;

    private const int NEGATIONS = 8_000;

    /// <summary>The variables of the lambda no array is made of: two megabytes of text.</summary>
    private const int LAMBDA_VARIABLES = 200_000;

    /// <summary>The depth of the recursion that reads the lambda: 16,383 of its 32,767 calls read it.</summary>
    private const int LAMBDA_DEPTH = 14;

    /// <summary>Twenty times the 0.5 s the twenty-five readings the budget pays for take.</summary>
    private const double LAMBDA_BOUND_SECONDS = 10;

    /// <summary>The steps of reading an array a lambda of one variable stands for, as they were before lists were paid for.</summary>
    private const int LAMBDA_READ_STEPS = 22;

    /// <summary>The steps of comparing the finite map a lambda of one variable stands for, as they were before lists were paid for.</summary>
    private const int LAMBDA_MAP_STEPS = 41;

    private const int SHIFTS = 240;

    /// <summary>
    /// The steps of one shift in its chain: 32,769 for the shift and as many for the bvor (a
    /// step for every 64 bits of the two operands), 16,385 for the shift amount written at the
    /// width, and seven terms and literals.
    /// </summary>
    private const int SHIFT_STEPS = 81_930;

    private const int CONVERSIONS = 1_000;

    /// <summary>
    /// The steps of one conversion in its chain: 17,409 for the conversion (a step for every 64
    /// bits of the integer and of the width), 2,049 for the bvor, and five terms.
    /// </summary>
    private const int CONVERSION_STEPS = 19_463;

    #endregion

    #region Negation Tests

    [Test]
    public void NegationOfALargeRationalCostsNothingOfItsSizeTest()
    {
        // (- a) over a rational of two numerals of 30,000 digits, eight thousand times over in a
        // chain of let. The negation of a number in lowest terms is in lowest terms: nothing is
        // to be computed. Measured: 0.02 s; when every negation reduced its fraction again, 7 to
        // 10 ms each, 80 s.
        const string problem = "(declare-const x Real)(assert (not (is_int x)))";
        var alone = SmtCheck.Validate(problem, Negations(0));

        var (chain, elapsed) = SmtTimed.Validate(problem, Negations(NEGATIONS));

        Assert.That(alone.Class, Is.EqualTo(SmtValidationClass.Validated), alone.ToString());
        Assert.That(chain.Class, Is.EqualTo(SmtValidationClass.Validated), chain.ToString());
        Assert.That(chain.Steps - alone.Steps, Is.EqualTo((long)NEGATION_STEPS * NEGATIONS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(LEAST_BOUND_SECONDS), $"{NEGATIONS} negations took {elapsed.TotalSeconds:F2} s");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(7)]
    public void NegationOfARationalGivesTheNumberOfTheOtherSignTest(int negations)
    {
        // An odd number of negations of two thirds is minus two thirds, an even number two thirds.
        var term = new StringBuilder();
        for (var index = 0; index < negations; index++)
            term.Append("(- ");

        term.Append("(/ 2 3)").Append(')', negations);
        var odd = negations % 2 != 0;

        Assert.That(SmtCheck.Holds($"(= {term} {(odd ? "(/ (- 2) 3)" : "(/ 2 3)")})").Class, Is.EqualTo(SmtValidationClass.Validated));
        Assert.That(SmtCheck.Holds($"(= (+ {term} (/ 2 3)) 0.0)").Class, Is.EqualTo(odd ? SmtValidationClass.Validated : SmtValidationClass.Failed));
    }

    #endregion

    #region Lambda Tests

    [Test]
    public void LambdaOfManyVariablesReadAtEveryCallEndsAtTheStepBudgetTest()
    {
        // A model's own function whose body holds a lambda of 200,000 variables. Such a lambda
        // is no value, but its list is read to the end before the evaluator says so, to tell a
        // list that is not well-formed from an array of many indices: 15 to 20 ms a reading.
        // Read for the one step of its term, the list was read at 16,383 calls inside the
        // budget, and the model validated after 240 to 320 s. Paid for variable by variable,
        // the budget ends the evaluation at the twenty-fifth reading, in 0.5 s.
        var (validation, elapsed) = SmtTimed.Validate("(declare-const x Bool)(assert x)", Lambdas(LAMBDA_VARIABLES, LAMBDA_DEPTH));

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        Assert.That(validation.Steps, Is.EqualTo(SmtEvaluationBudget.DEFAULT_STEPS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(LAMBDA_BOUND_SECONDS), $"the budget was used up in {elapsed.TotalSeconds:F2} s");
    }

    [Test]
    public void LambdaOfManyVariablesPaysForEveryVariableTest()
    {
        var two = SmtCheck.Holds($"(or (lambda ({Variables(2)}) 0) true)");
        var thousand = SmtCheck.Holds($"(or (lambda ({Variables(1000)}) 0) true)");

        // The disjunct beside it decides: the lambda is only a term the evaluator has no value for.
        Assert.That(two.Class, Is.EqualTo(SmtValidationClass.Validated), two.ToString());
        Assert.That(thousand.Class, Is.EqualTo(SmtValidationClass.Validated), thousand.ToString());
        Assert.That(thousand.Steps - two.Steps, Is.EqualTo(998 * SmtTerms.VARIABLE_STEPS));
    }

    [Test]
    public void LambdaOfOneVariableCostsWhatItDidTest()
    {
        // The only lambda that is a value, and the only one a solver prints, pays nothing new:
        // these are the steps of the evaluator before any list was paid for.
        Assert.That(SmtCheck.Holds("(= (select (lambda ((i Int)) (* i i)) 7) 49)").Steps, Is.EqualTo(LAMBDA_READ_STEPS));
        Assert.That(SmtCheck.Holds("(= (lambda ((i Int)) (ite (= i 1) 5 0)) (store ((as const (Array Int Int)) 0) 1 5))").Steps, Is.EqualTo(LAMBDA_MAP_STEPS));
    }

    [TestCase("(or (lambda ((x Int) (x Int)) 0) true)")]
    [TestCase("(or (lambda ((x Int) (true Int)) 0) true)")]
    [TestCase("(or (lambda ((x Int) y) 0) true)")]
    [TestCase("(or (lambda () 0) true)")]
    public void LambdaWhoseVariablesAreNotWellFormedIsStillATermThatFitsNoReadingTest(string assertion)
    {
        // Not passed by the disjunction, as before: the list is still read to the end.
        var validation = SmtCheck.Holds(assertion);

        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial));
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    #endregion

    #region Bit-vector Tests

    [Test]
    public void ShiftOfAWideBitVectorPaysForItsWordsAndEndsInTimeTest()
    {
        // (bvshl v k) of a million bits by half a million, 240 times: all the budget pays for.
        // What is shifted out is cut off by keeping the low bits. Measured: 0.1 s; cut off by a
        // division, 2 ms a shift, 0.5 s. This one never ran past the half second the budget
        // stands for: the test holds the bound and the steps, which the cut does not change.
        const string problem = "(declare-const x (_ BitVec 1048576))(assert (= x x))";
        var alone = SmtCheck.Validate(problem, Shifts(0));

        var (chain, elapsed) = SmtTimed.Validate(problem, Shifts(SHIFTS));

        Assert.That(chain.Class, Is.EqualTo(SmtValidationClass.Validated), chain.ToString());
        Assert.That(chain.Steps - alone.Steps, Is.EqualTo((long)SHIFT_STEPS * SHIFTS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(LEAST_BOUND_SECONDS), $"{SHIFTS} shifts took {elapsed.TotalSeconds:F2} s");
    }

    [Test]
    public void IntegerIntoANarrowBitVectorPaysForItsWordsAndEndsInTimeTest()
    {
        // ((_ int2bv 65536) n) of an integer of a million bits, a thousand times: all the budget
        // pays for, at the width where a division cost most for the steps charged. Measured:
        // 0.08 s; by a division, 1.4 ms each, 1.4 s, three times the half second the budget
        // stands for and still inside the bound here: what tells the two apart is the test of
        // the reduction itself, which times it against keeping the low bits.
        const string problem = "(declare-const x (_ BitVec 65536))(assert (= x x))";
        var alone = SmtCheck.Validate(problem, Conversions(0));

        var (chain, elapsed) = SmtTimed.Validate(problem, Conversions(CONVERSIONS));

        Assert.That(chain.Class, Is.EqualTo(SmtValidationClass.Validated), chain.ToString());
        Assert.That(chain.Steps - alone.Steps, Is.EqualTo((long)CONVERSION_STEPS * CONVERSIONS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(LEAST_BOUND_SECONDS), $"{CONVERSIONS} conversions took {elapsed.TotalSeconds:F2} s");
    }

    #endregion

    #region Tools

    /// <summary>
    /// The model that gives <c>x</c> a bit-vector of a million bits, shifted left by half a
    /// million so many times in a chain of <c>let</c>.
    /// </summary>
    private static string Shifts(int count)
    {
        var model = new StringBuilder("((define-fun x () (_ BitVec 1048576) (let ((v ((_ repeat 16384) #xffffffffffffffff))) (let ((w v)) ");
        for (var index = 0; index < count; index++)
            model.Append("(let ((w (bvor (bvshl v (_ bv524288 1048576)) w))) ");

        return model.Append('w').Append(')', count + 2).Append("))").ToString();
    }

    /// <summary>
    /// The model that gives <c>x</c> the low 65,536 bits of an integer of a million bits, taken
    /// so many times in a chain of <c>let</c>.
    /// </summary>
    private static string Conversions(int count)
    {
        var model = new StringBuilder("((define-fun x () (_ BitVec 65536) (let ((n (bv2nat ((_ repeat 16384) #xffffffffffffffff)))) (let ((w ((_ int2bv 65536) n))) ");
        for (var index = 0; index < count; index++)
            model.Append("(let ((w (bvor ((_ int2bv 65536) n) w))) ");

        return model.Append('w').Append(')', count + 2).Append("))").ToString();
    }

    /// <summary>
    /// The model whose own function calls itself twice a level, down from a depth, and reads a
    /// lambda of so many variables at every call that calls on.
    /// </summary>
    private static string Lambdas(int variables, int depth)
    {
        return $"((define-fun g!1 ((n Int)) Bool (ite (<= n 0) true (or (lambda ({Variables(variables)}) 0) (and (g!1 (- n 1)) (g!1 (- n 1)))))) (define-fun x () Bool (g!1 {depth})))";
    }

    /// <summary>
    /// So many sorted variables, each of a name of its own.
    /// </summary>
    private static string Variables(int count)
    {
        var variables = new StringBuilder();
        for (var index = 0; index < count; index++)
            variables.Append("(v").Append(index).Append(" Int)");

        return variables.ToString();
    }

    /// <summary>
    /// The model that gives <c>x</c> a rational of two large numerals, negated so many times in
    /// a chain of <c>let</c>, each in the body of the one before.
    /// </summary>
    private static string Negations(int count)
    {
        var model = new StringBuilder("((define-fun x () Real (let ((a (/ ");
        model.Append(Digits(1, RATIONAL_DIGITS)).Append(' ').Append(Digits(2, RATIONAL_DIGITS)).Append("))) ");
        for (var index = 0; index < count; index++)
            model.Append("(let ((a (- a))) ");

        return model.Append('a').Append(')', count + 1).Append("))").ToString();
    }

    /// <summary>
    /// A numeral of so many digits, the same for the same seed on every machine: the digits of a
    /// generator, so that two numerals of two seeds share no factor to speak of.
    /// </summary>
    private static string Digits(ulong seed, int count)
    {
        var state = seed;
        var digits = new StringBuilder(count);
        while (digits.Length < count)
        {
            state = state * 6364136223846793005UL + 1442695040888963407UL;
            var digit = (int)((state >> 33) % 10);

            // A numeral has no leading zero.
            digits.Append((char)('0' + (digits.Length == 0 && digit == 0 ? 1 : digit)));
        }

        return digits.ToString();
    }

    #endregion
}
