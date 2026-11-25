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

    #region Tools

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
