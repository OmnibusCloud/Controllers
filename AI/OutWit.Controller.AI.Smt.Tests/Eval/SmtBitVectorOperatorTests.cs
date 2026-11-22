using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// Every bit-vector operator against a reference written here in plain integer arithmetic,
/// over a few hundred operand pairs at the widths 1, 7, 8, 64, 65 and 256. The reference
/// computes on the signed and unsigned numbers the bits stand for; the evaluator computes
/// the standard's definitions case by case. They share no code.
/// </summary>
[TestFixture]
public class SmtBitVectorOperatorTests
{
    #region Arithmetic Tests

    [TestCase("bvadd")]
    [TestCase("bvsub")]
    [TestCase("bvmul")]
    [TestCase("bvand")]
    [TestCase("bvor")]
    [TestCase("bvxor")]
    [TestCase("bvnand")]
    [TestCase("bvnor")]
    [TestCase("bvxnor")]
    [TestCase("bvudiv")]
    [TestCase("bvurem")]
    [TestCase("bvsdiv")]
    [TestCase("bvsrem")]
    [TestCase("bvsmod")]
    [TestCase("bvshl")]
    [TestCase("bvlshr")]
    [TestCase("bvashr")]
    public void BinaryOperatorAgreesWithIntegerArithmeticTest(string name)
    {
        foreach (var (width, left, right) in SmtBitVectorSamples.Pairs())
        {
            var expected = SmtBitVectorSamples.Unsigned(Reference(name, left, right, width), width);

            var actual = Vector($"({name} {SmtBitVectorSamples.Literal(left, width)} {SmtBitVectorSamples.Literal(right, width)})");

            Assert.That(actual.Width, Is.EqualTo(width));
            Assert.That(actual.Value, Is.EqualTo(expected), $"({name} {left} {right}) at width {width}");
        }
    }

    [TestCase("bvult")]
    [TestCase("bvule")]
    [TestCase("bvugt")]
    [TestCase("bvuge")]
    [TestCase("bvslt")]
    [TestCase("bvsle")]
    [TestCase("bvsgt")]
    [TestCase("bvsge")]
    public void ComparisonAgreesWithIntegerOrderTest(string name)
    {
        foreach (var (width, left, right) in SmtBitVectorSamples.Pairs())
        {
            var signed = name[2] == 's';
            var order = signed
                ? SmtBitVectorSamples.Signed(left, width).CompareTo(SmtBitVectorSamples.Signed(right, width))
                : left.CompareTo(right);
            var expected = name[3..] switch
            {
                "lt" => order < 0,
                "le" => order <= 0,
                "gt" => order > 0,
                _ => order >= 0
            };

            var actual = SmtCheck.Evaluate($"({name} {SmtBitVectorSamples.Literal(left, width)} {SmtBitVectorSamples.Literal(right, width)})");

            Assert.That(((SmtBoolValue)actual).Value, Is.EqualTo(expected), $"({name} {left} {right}) at width {width}");
        }
    }

    [Test]
    public void UnaryOperatorsAgreeWithIntegerArithmeticTest()
    {
        foreach (var (width, value, _) in SmtBitVectorSamples.Pairs())
        {
            var literal = SmtBitVectorSamples.Literal(value, width);

            Assert.That(Vector($"(bvnot {literal})").Value, Is.EqualTo(SmtBitVectorSamples.Unsigned(-value - 1, width)));
            Assert.That(Vector($"(bvneg {literal})").Value, Is.EqualTo(SmtBitVectorSamples.Unsigned(-value, width)));
            Assert.That(((SmtIntValue)SmtCheck.Evaluate($"(bv2nat {literal})")).Value, Is.EqualTo(value));
            Assert.That(((SmtIntValue)SmtCheck.Evaluate($"(ubv_to_int {literal})")).Value, Is.EqualTo(value));
            Assert.That(((SmtIntValue)SmtCheck.Evaluate($"(sbv_to_int {literal})")).Value, Is.EqualTo(SmtBitVectorSamples.Signed(value, width)));
        }
    }

    [Test]
    public void BvcompIsOneBitOfEqualityTest()
    {
        foreach (var (width, left, right) in SmtBitVectorSamples.Pairs())
        {
            var actual = Vector($"(bvcomp {SmtBitVectorSamples.Literal(left, width)} {SmtBitVectorSamples.Literal(right, width)})");

            Assert.That(actual.Width, Is.EqualTo(1));
            Assert.That(actual.Value.IsOne, Is.EqualTo(left == right));
        }
    }

    #endregion

    #region Shape Tests

    [Test]
    public void ShapeOperatorsAgreeWithTheBitsWrittenOutTest()
    {
        foreach (var (width, left, right) in SmtBitVectorSamples.Pairs())
        {
            var bits = SmtBitVectorSamples.Bits(left, width);
            var other = SmtBitVectorSamples.Bits(right, width);
            var literal = SmtBitVectorSamples.Literal(left, width);
            var amount = (int)(right % (3 * width));
            var high = (int)(right % width);
            var low = (int)(left % (high + 1));

            Assert.That(Bits($"(concat {literal} {SmtBitVectorSamples.Literal(right, width)})"), Is.EqualTo(bits + other));
            Assert.That(Bits($"((_ extract {high} {low}) {literal})"), Is.EqualTo(bits.Substring(width - 1 - high, high - low + 1)));
            Assert.That(Bits($"((_ zero_extend {amount}) {literal})"), Is.EqualTo(new string('0', amount) + bits));
            Assert.That(Bits($"((_ sign_extend {amount}) {literal})"), Is.EqualTo(new string(bits[0], amount) + bits));
            Assert.That(Bits($"((_ repeat {amount % 5 + 1}) {literal})"), Is.EqualTo(string.Concat(Enumerable.Repeat(bits, amount % 5 + 1))));
            Assert.That(Bits($"((_ rotate_left {amount}) {literal})"), Is.EqualTo(bits[(amount % width)..] + bits[..(amount % width)]));
            Assert.That(Bits($"((_ rotate_right {amount}) {literal})"), Is.EqualTo(bits[^(amount % width)..] + bits[..^(amount % width)]));
        }
    }

    [Test]
    public void Int2bvReducesAnIntegerModuloTwoToTheWidthTest()
    {
        foreach (var (width, left, right) in SmtBitVectorSamples.Pairs())
        {
            // An integer of any size and either sign: the product, and its negation.
            var number = left * right + left;
            var negative = $"(- {number})";

            Assert.That(Vector($"((_ int2bv {width}) {number})").Value, Is.EqualTo(SmtBitVectorSamples.Unsigned(number, width)));
            Assert.That(Vector($"((_ int2bv {width}) {negative})").Value, Is.EqualTo(SmtBitVectorSamples.Unsigned(-number, width)));
            Assert.That(Vector($"((_ int_to_bv {width}) {negative})").Value, Is.EqualTo(SmtBitVectorSamples.Unsigned(-number, width)));
        }
    }

    [Test]
    public void EverySpellingOfAValueIsTheSameValueTest()
    {
        Assert.That(Vector("#x0f").Value, Is.EqualTo(new BigInteger(15)));
        Assert.That(Vector("#x0f").Width, Is.EqualTo(8));
        Assert.That(Vector("#b00001111").Width, Is.EqualTo(8));
        Assert.That(Vector("#xFf").Value, Is.EqualTo(new BigInteger(255)));
        Assert.That(((SmtBoolValue)SmtCheck.Evaluate("(= #x0f #b00001111 (_ bv15 8))")).Value, Is.True);
        Assert.That(((SmtBoolValue)SmtCheck.Evaluate("(= #x8000000000000000 (_ bv9223372036854775808 64))")).Value, Is.True);
        Assert.That(Vector("#b1").Value.IsOne, Is.True);
    }

    [Test]
    public void NaryOperatorsFoldFromTheLeftTest()
    {
        Assert.That(Vector("(bvadd #x01 #x02 #x03)").Value, Is.EqualTo(new BigInteger(6)));
        Assert.That(Vector("(bvmul #xff #xff #xff)").Value, Is.EqualTo(new BigInteger(255)));
        Assert.That(Vector("(bvand #x0f #x3c #xf8)").Value, Is.EqualTo(new BigInteger(8)));
        Assert.That(Vector("(bvor #x01 #x02 #x04)").Value, Is.EqualTo(new BigInteger(7)));
        Assert.That(Vector("(bvxor #x01 #x03 #x07)").Value, Is.EqualTo(new BigInteger(5)));
        Assert.That(Bits("(concat #b1 #b00 #b111)"), Is.EqualTo("100111"));
    }

    #endregion

    #region Refusal Tests

    [TestCase("(bvadd #x01 #b1)")]
    [TestCase("(bvadd #x01)")]
    [TestCase("(bvsub #x01 #x01 #x01)")]
    [TestCase("(bvnot #x01 #x01)")]
    [TestCase("(bvult #x01 #x01 #x01)")]
    [TestCase("(bvult #x01 #x0001)")]
    [TestCase("(bvadd #x01 1)")]
    [TestCase("(concat #x01)")]
    [TestCase("((_ extract 8 0) #x01)")]
    [TestCase("((_ extract 0 1) #x01)")]
    [TestCase("((_ extract 1) #x01)")]
    [TestCase("((_ repeat 0) #x01)")]
    [TestCase("((_ zero_extend 1 2) #x01)")]
    [TestCase("((_ int2bv 0) 5)")]
    [TestCase("((_ int2bv 8) #x05)")]
    [TestCase("(_ bv256 8)")]
    [TestCase("(_ bv1 0)")]
    [TestCase("(_ bv01 8)")]
    [TestCase("(bv2nat 5)")]
    public void ApplicationOutsideTheSignatureIsNoValueTest(string term)
    {
        Assert.That(SmtCheck.ReasonOf(term), Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
    }

    #endregion

    #region Tools

    /// <summary>
    /// The operators on the numbers the bits stand for. Division by zero is the standard's:
    /// all ones for the unsigned quotient, the dividend for the remainders, and for the signed
    /// quotient one or minus one against the dividend's sign.
    /// </summary>
    private static BigInteger Reference(string name, BigInteger left, BigInteger right, int width)
    {
        var signedLeft = SmtBitVectorSamples.Signed(left, width);
        var signedRight = SmtBitVectorSamples.Signed(right, width);
        return name switch
        {
            "bvadd" => left + right,
            "bvsub" => left - right,
            "bvmul" => left * right,
            "bvand" => left & right,
            "bvor" => left | right,
            "bvxor" => left ^ right,
            "bvnand" => -(left & right) - 1,
            "bvnor" => -(left | right) - 1,
            "bvxnor" => -(left ^ right) - 1,
            "bvudiv" => right.IsZero ? -1 : BigInteger.Divide(left, right),
            "bvurem" => right.IsZero ? left : BigInteger.Remainder(left, right),
            "bvsdiv" => right.IsZero ? (signedLeft < 0 ? 1 : -1) : BigInteger.Divide(signedLeft, signedRight),
            "bvsrem" => right.IsZero ? signedLeft : BigInteger.Remainder(signedLeft, signedRight),
            "bvsmod" => right.IsZero ? signedLeft : FloorRemainder(signedLeft, signedRight),
            "bvshl" => right >= width ? 0 : left << (int)right,
            "bvlshr" => right >= width ? 0 : left >> (int)right,
            "bvashr" => right >= width ? (signedLeft < 0 ? -1 : 0) : signedLeft >> (int)right,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    /// <summary>
    /// The remainder with the sign of the divisor: what <c>bvsmod</c> is on signed numbers.
    /// </summary>
    private static BigInteger FloorRemainder(BigInteger dividend, BigInteger divisor)
    {
        var remainder = BigInteger.Remainder(dividend, divisor);
        return !remainder.IsZero && remainder.Sign != divisor.Sign ? remainder + divisor : remainder;
    }

    private static SmtBitVectorValue Vector(string term)
    {
        return (SmtBitVectorValue)SmtCheck.Evaluate(term);
    }

    private static string Bits(string term)
    {
        var vector = Vector(term);
        return SmtBitVectorSamples.Bits(vector.Value, vector.Width);
    }

    #endregion
}
