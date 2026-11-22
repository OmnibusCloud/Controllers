using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtArithmeticOperatorTests
{
    #region Constants

    private const int SEED = 20261122;
    private const int ROUNDS = 400;

    #endregion

    #region Integer Division Tests

    [TestCase(7, 2, 3, 1)]
    [TestCase(-7, 2, -4, 1)]
    [TestCase(7, -2, -3, 1)]
    [TestCase(-7, -2, 4, 1)]
    [TestCase(6, 3, 2, 0)]
    [TestCase(-6, 3, -2, 0)]
    [TestCase(6, -3, -2, 0)]
    [TestCase(-6, -3, 2, 0)]
    [TestCase(0, 5, 0, 0)]
    [TestCase(0, -5, 0, 0)]
    [TestCase(1, 5, 0, 1)]
    [TestCase(-1, 5, -1, 4)]
    [TestCase(1, -5, 0, 1)]
    [TestCase(-1, -5, 1, 4)]
    public void DivAndModFollowTheSignOfTheDivisorTest(int dividend, int divisor, int quotient, int remainder)
    {
        Assert.That(Integer($"(div {Term(dividend)} {Term(divisor)})"), Is.EqualTo(new BigInteger(quotient)));
        Assert.That(Integer($"(mod {Term(dividend)} {Term(divisor)})"), Is.EqualTo(new BigInteger(remainder)));
    }

    [Test]
    public void DivAndModSatisfyTheStandardsDefinitionTest()
    {
        // The standard defines div and mod by two conditions that hold for exactly one pair:
        // m = n * q + r, and 0 <= r < |n|. Checked here on numbers of every sign and size.
        var random = new Random(SEED);
        for (var round = 0; round < ROUNDS; round++)
        {
            var dividend = Next(random, round % 7 == 0 ? 200 : 40);
            var divisor = Next(random, round % 5 == 0 ? 3 : 30);
            if (divisor.IsZero)
                divisor = BigInteger.One;

            var quotient = Integer($"(div {Term(dividend)} {Term(divisor)})");
            var remainder = Integer($"(mod {Term(dividend)} {Term(divisor)})");

            Assert.That(divisor * quotient + remainder, Is.EqualTo(dividend), $"{dividend} by {divisor}");
            Assert.That(remainder.Sign, Is.GreaterThanOrEqualTo(0), $"{dividend} by {divisor}");
            Assert.That(remainder, Is.LessThan(BigInteger.Abs(divisor)), $"{dividend} by {divisor}");
        }
    }

    [Test]
    public void DivChainsFromTheLeftTest()
    {
        Assert.That(Integer("(div 100 7 2)"), Is.EqualTo(new BigInteger(7)));
        Assert.That(Integer("(div (- 100) 7 2)"), Is.EqualTo(new BigInteger(-8)));
    }

    [TestCase("(div 1 0)")]
    [TestCase("(mod 1 0)")]
    [TestCase("(/ 1 0)")]
    [TestCase("(/ 1.5 0.0)")]
    [TestCase("(/ 1 2 (- 1 1))")]
    [TestCase("((_ divisible 3) (div 1 0))")]
    public void DivisionByZeroIsLeftToTheModelTest(string term)
    {
        Assert.That(SmtCheck.ReasonOf(term), Is.EqualTo(SmtValidationReasons.DIVISION_BY_ZERO));
    }

    [Test]
    public void DivisibleIsARemainderOfZeroTest()
    {
        Assert.That(Truth("((_ divisible 3) 9)"), Is.True);
        Assert.That(Truth("((_ divisible 3) (- 9))"), Is.True);
        Assert.That(Truth("((_ divisible 3) 10)"), Is.False);
        Assert.That(Truth("((_ divisible 1) 0)"), Is.True);
    }

    #endregion

    #region Rational Tests

    [Test]
    public void RationalArithmeticIsExactTest()
    {
        var random = new Random(SEED);
        for (var round = 0; round < ROUNDS; round++)
        {
            var a = Next(random, 50);
            var b = Positive(random, 50);
            var c = Next(random, 50);
            var d = Positive(random, 50);
            var left = $"(/ {Term(a)} {Term(b)})";
            var right = $"(/ {Term(c)} {Term(d)})";

            AssertFraction($"(+ {left} {right})", a * d + c * b, b * d);
            AssertFraction($"(- {left} {right})", a * d - c * b, b * d);
            AssertFraction($"(* {left} {right})", a * c, b * d);
            AssertFraction($"(- {left})", -a, b);
            if (!c.IsZero)
                AssertFraction($"(/ {left} {right})", a * d, b * c);

            Assert.That(Truth($"(< {left} {right})"), Is.EqualTo(a * d < c * b));
            Assert.That(Truth($"(>= {left} {right})"), Is.EqualTo(a * d >= c * b));
            Assert.That(Truth($"(= {left} {right})"), Is.EqualTo(a * d == c * b));
        }
    }

    [Test]
    public void DecimalIsTheRationalItsDigitsWriteTest()
    {
        AssertFraction("1.50", 3, 2);
        AssertFraction("0.125", 1, 8);
        AssertFraction("10.0", 10, 1);
        AssertFraction("(- 0.75)", -3, 4);
        AssertFraction("0.000001", 1, 1000000);
        AssertFraction("(+ 0.1 0.2)", 3, 10);
    }

    [Test]
    public void IntegerArithmeticHasNoWordSizeTest()
    {
        var large = BigInteger.Pow(10, 60) + 7;

        Assert.That(Integer($"(* {large} {large})"), Is.EqualTo(large * large));
        Assert.That(Integer($"(- {large} (* 2 {large}))"), Is.EqualTo(-large));
        Assert.That(Integer($"(+ {large} 1 2 3)"), Is.EqualTo(large + 6));
        Assert.That(Integer($"(abs (- {large}))"), Is.EqualTo(large));
        Assert.That(Integer($"(mod (- {large}) 1000)"), Is.EqualTo(new BigInteger(993)));
    }

    [Test]
    public void IntegersStayIntegersAndOneRealMakesARealTest()
    {
        Assert.That(SmtCheck.Evaluate("(+ 1 2)"), Is.InstanceOf<SmtIntValue>());
        Assert.That(SmtCheck.Evaluate("(* 2 (- 3))"), Is.InstanceOf<SmtIntValue>());
        Assert.That(SmtCheck.Evaluate("(+ 1 2.0)"), Is.InstanceOf<SmtRealValue>());
        Assert.That(SmtCheck.Evaluate("(/ 4 2)"), Is.InstanceOf<SmtRealValue>());
        Assert.That(SmtCheck.Evaluate("(to_real 4)"), Is.InstanceOf<SmtRealValue>());
        Assert.That(SmtCheck.Evaluate("(to_int 4.5)"), Is.InstanceOf<SmtIntValue>());
    }

    [Test]
    public void AnIntAndARealOfOneNumberAreEqualTest()
    {
        // Solvers that mix the sorts convert the Int: the two are compared by their number.
        Assert.That(Truth("(= 2 2.0)"), Is.True);
        Assert.That(Truth("(= (/ 4 2) 2)"), Is.True);
        Assert.That(Truth("(distinct 1 1.0)"), Is.False);
        Assert.That(Truth("(< 1 1.5 2)"), Is.True);
    }

    [Test]
    public void ToIntRoundsTowardMinusInfinityTest()
    {
        Assert.That(Integer("(to_int 2.5)"), Is.EqualTo(new BigInteger(2)));
        Assert.That(Integer("(to_int (- 2.5))"), Is.EqualTo(new BigInteger(-3)));
        Assert.That(Integer("(to_int (- 2.0))"), Is.EqualTo(new BigInteger(-2)));
        Assert.That(Truth("(is_int (/ 6 3))"), Is.True);
        Assert.That(Truth("(is_int (/ 6 4))"), Is.False);
        Assert.That(Truth("(is_int 3)"), Is.True);
    }

    [Test]
    public void ComparisonsChainTest()
    {
        Assert.That(Truth("(< 1 2 3)"), Is.True);
        Assert.That(Truth("(< 1 3 2)"), Is.False);
        Assert.That(Truth("(<= 1 1 2)"), Is.True);
        Assert.That(Truth("(> 3 2 2)"), Is.False);
        Assert.That(Truth("(>= 3 2 2)"), Is.True);
    }

    #endregion

    #region Refusal Tests

    [TestCase("(div 7.0 2)")]
    [TestCase("(mod 7 2.0)")]
    [TestCase("(mod 7 2 2)")]
    [TestCase("(div 7)")]
    [TestCase("(+ 1 true)")]
    [TestCase("(< 1)")]
    [TestCase("(< 1 #x01)")]
    [TestCase("(abs 1 2)")]
    [TestCase("(abs 2.5)")]
    [TestCase("(to_real 2.5)")]
    [TestCase("(to_real 1 2)")]
    [TestCase("(to_int 1 2)")]
    [TestCase("((_ divisible 0) 4)")]
    [TestCase("((_ divisible 2) 4.0)")]
    [TestCase("(= 1 true)")]
    [TestCase("(= 1 #x01)")]
    [TestCase("(not 1)")]
    [TestCase("(ite 1 2 3)")]
    public void ApplicationOutsideTheSignatureIsNoValueTest(string term)
    {
        Assert.That(SmtCheck.ReasonOf(term), Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    #endregion

    #region Tools

    private static void AssertFraction(string term, BigInteger numerator, BigInteger denominator)
    {
        var value = ((SmtRealValue)SmtCheck.Evaluate(term)).Value;

        // The same number, and in lowest terms with a positive denominator.
        Assert.That(value.Numerator * denominator, Is.EqualTo(numerator * value.Denominator), term);
        Assert.That(value.Denominator.Sign, Is.EqualTo(1), term);
        Assert.That(BigInteger.GreatestCommonDivisor(value.Numerator, value.Denominator), Is.EqualTo(BigInteger.One), term);
    }

    private static BigInteger Integer(string term)
    {
        return ((SmtIntValue)SmtCheck.Evaluate(term)).Value;
    }

    private static bool Truth(string term)
    {
        return ((SmtBoolValue)SmtCheck.Evaluate(term)).Value;
    }

    /// <summary>
    /// A number as a solver writes it: a negative one as the negation of a numeral.
    /// </summary>
    private static string Term(BigInteger number)
    {
        return number.Sign < 0 ? $"(- {-number})" : number.ToString();
    }

    private static BigInteger Next(Random random, int bits)
    {
        var bytes = new byte[bits / 8 + 1];
        random.NextBytes(bytes);
        return new BigInteger(bytes);
    }

    private static BigInteger Positive(Random random, int bits)
    {
        return BigInteger.Abs(Next(random, bits)) + 1;
    }

    #endregion
}
