using System.Diagnostics;
using System.Numerics;
using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// A bit-vector made of any integer: the integer modulo two to the width, bit for bit what the
/// remainder of a division gives, and made at the price of keeping the low bits, not of a
/// division.
/// </summary>
[TestFixture]
public class SmtBitVectorValueTests
{
    #region Constants

    private const int SEED = 20261125;
    private const int SAMPLES = 4000;
    private const int MAX_WIDTH = 300;
    private const int MAX_VALUE_BYTES = 120;

    /// <summary>The bits of the wide integer whose reduction is timed: the ceiling of an integer.</summary>
    private const int WIDE_BITS = 1_048_576;

    /// <summary>The width it is reduced to: a quarter of its own, where a division costs most.</summary>
    private const int NARROW_WIDTH = 262_144;

    private const int ROUNDS = 20;

    /// <summary>
    /// How many times the time of keeping the low bits the reduction may take. Measured: 1.1
    /// times for a number that is not negative and 1.3 for a negative one; by a division, 24
    /// and 23 times.
    /// </summary>
    private const double MOST_MASKS = 8;

    private static readonly int[] WIDTHS = [1, 2, 7, 8, 31, 32, 33, 63, 64, 65, 128, 257];

    #endregion

    #region Wrap Tests

    [Test]
    public void WrapIsTheRemainderOfTheDivisionByTwoToTheWidthTest()
    {
        var random = new Random(SEED);
        for (var sample = 0; sample < SAMPLES; sample++)
        {
            var width = sample % 3 == 0 ? WIDTHS[random.Next(WIDTHS.Length)] : random.Next(1, MAX_WIDTH);
            var bytes = new byte[random.Next(1, MAX_VALUE_BYTES)];
            random.NextBytes(bytes);

            AssertWrap(new BigInteger(bytes), width);
        }
    }

    [Test]
    public void WrapAtTheEdgesOfEveryWidthIsTheRemainderTest()
    {
        foreach (var width in WIDTHS)
        {
            var modulus = BigInteger.One << width;
            foreach (var multiple in new[] { 0, 1, 2, 5 })
            {
                foreach (var offset in new[] { -2, -1, 0, 1, 2 })
                {
                    AssertWrap(modulus * multiple + offset, width);
                    AssertWrap(-(modulus * multiple + offset), width);
                }
            }
        }
    }

    [Test]
    public void WrapOfAValueInRangeIsTheValueTest()
    {
        var value = new BigInteger(0xA5);

        var wrapped = SmtBitVectorValue.Wrap(value, 8);

        Assert.That(wrapped.Value, Is.EqualTo(value));
        Assert.That(wrapped.Width, Is.EqualTo(8));
        Assert.That(SmtBitVectorValue.Wrap(BigInteger.MinusOne, 8).Value, Is.EqualTo(new BigInteger(255)));
        Assert.That(SmtBitVectorValue.Wrap(new BigInteger(256), 8).Value.IsZero, Is.True);
        Assert.That(SmtBitVectorValue.Wrap(new BigInteger(-256), 8).Value.IsZero, Is.True);
        Assert.That(SmtBitVectorValue.Wrap(BigInteger.MinusOne, 1).Value.IsOne, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WrapOfAWideIntegerCostsAboutWhatKeepingItsLowBitsCostsTest(bool negative)
    {
        // An integer of a million bits into a quarter of them. Two to the width is a power of
        // two, so the reduction is the low bits: a pass over the number. Both sides are timed
        // here, on the same number and the same machine, the fastest of twenty rounds each, so
        // that only their ratio is asserted.
        var bytes = new byte[WIDE_BITS / 8];
        new Random(SEED).NextBytes(bytes);
        bytes[^1] = 0x7F;
        var value = negative ? -new BigInteger(bytes) : new BigInteger(bytes);
        var mask = (BigInteger.One << NARROW_WIDTH) - BigInteger.One;

        var masking = Fastest(() => (value & mask).IsZero);
        var wrapping = Fastest(() => SmtBitVectorValue.Wrap(value, NARROW_WIDTH).Value.IsZero);

        Assert.That(wrapping.TotalMilliseconds, Is.LessThan(MOST_MASKS * masking.TotalMilliseconds),
            $"the reduction took {wrapping.TotalMilliseconds:F3} ms, keeping the low bits {masking.TotalMilliseconds:F3} ms");
    }

    #endregion

    #region Tools

    private static void AssertWrap(BigInteger value, int width)
    {
        var modulus = BigInteger.One << width;
        var expected = (value % modulus + modulus) % modulus;

        var wrapped = SmtBitVectorValue.Wrap(value, width);

        Assert.That(wrapped.Width, Is.EqualTo(width));
        Assert.That(wrapped.Value, Is.EqualTo(expected), $"{value} at width {width}");
    }

    private static TimeSpan Fastest(Func<bool> work)
    {
        var fastest = TimeSpan.MaxValue;
        for (var round = 0; round < ROUNDS; round++)
        {
            var watch = Stopwatch.StartNew();
            work();
            watch.Stop();
            if (watch.Elapsed < fastest)
                fastest = watch.Elapsed;
        }

        return fastest;
    }

    #endregion
}
