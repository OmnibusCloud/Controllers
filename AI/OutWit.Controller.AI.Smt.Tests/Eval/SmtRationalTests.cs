using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The exact rationals: what an operator gives is the number in lowest terms, with a positive
/// denominator, whichever way it was made.
/// </summary>
[TestFixture]
public class SmtRationalTests
{
    #region Constants

    private const int SEED = 20261125;
    private const int SAMPLES = 500;
    private const int MAX_BYTES = 40;

    #endregion

    #region Negation Tests

    [Test]
    public void NegationTurnsTheSignAndKeepsTheDenominatorTest()
    {
        var negated = -new SmtRational(6, 4);

        Assert.That(negated.Numerator, Is.EqualTo(new BigInteger(-3)));
        Assert.That(negated.Denominator, Is.EqualTo(new BigInteger(2)));
        Assert.That(negated.Sign, Is.EqualTo(-1));
        Assert.That(negated.IsInteger, Is.False);
    }

    [Test]
    public void NegationOfZeroAndOfAWholeNumberIsWholeTest()
    {
        var zero = -new SmtRational(BigInteger.Zero);
        var whole = -new SmtRational(new BigInteger(7));

        Assert.That(zero.Numerator.IsZero, Is.True);
        Assert.That(zero.Denominator.IsOne, Is.True);
        Assert.That(whole.Numerator, Is.EqualTo(new BigInteger(-7)));
        Assert.That(whole.IsInteger, Is.True);
    }

    [Test]
    public void NegationIsTheFractionOfTheOtherSignInLowestTermsTest()
    {
        // Against the constructor that reduces: the negation of a number in lowest terms is in
        // lowest terms, part for part, and negating twice gives the number back.
        var random = new Random(SEED);
        for (var sample = 0; sample < SAMPLES; sample++)
        {
            var numerator = Integer(random);
            var denominator = Integer(random);
            if (denominator.IsZero)
                denominator = BigInteger.One;

            var value = new SmtRational(numerator, denominator);
            var expected = new SmtRational(-numerator, denominator);

            var negated = -value;

            Assert.That(negated.Numerator, Is.EqualTo(expected.Numerator), $"-({numerator}/{denominator})");
            Assert.That(negated.Denominator, Is.EqualTo(expected.Denominator), $"-({numerator}/{denominator})");
            Assert.That(negated.Denominator.Sign, Is.EqualTo(1));
            Assert.That((-negated).Equals(value), Is.True);
            Assert.That((value + negated).Sign, Is.EqualTo(0));
        }
    }

    #endregion

    #region Tools

    /// <summary>
    /// An integer of either sign and of up to a few hundred bits; small ones and zero among them.
    /// </summary>
    private static BigInteger Integer(Random random)
    {
        var bytes = new byte[random.Next(1, MAX_BYTES)];
        random.NextBytes(bytes);
        return new BigInteger(bytes);
    }

    #endregion
}
