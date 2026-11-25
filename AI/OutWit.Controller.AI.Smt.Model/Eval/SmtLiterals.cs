using System.Globalization;
using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The values of the literals: a numeral, a decimal, a hexadecimal or binary literal, a string
/// literal, and a bit-vector written in decimal. A literal's size is checked and paid for
/// before it is read, and its text is checked again here: an s-expression made by a program
/// may carry a token no reader would have let through.
/// </summary>
internal static class SmtLiterals
{
    #region Constants

    /// <summary>
    /// A number of fewer digits than this fits a machine word, and is read as one. Looking at
    /// so many characters of digits costs a step.
    /// </summary>
    public const int WORD_DIGITS = 18;

    /// <summary>
    /// The steps 64 digits of a binary literal cost. A binary literal is read a character a
    /// bit, where a hexadecimal one is read a character for four bits: reading 64 binary
    /// digits was measured at the time of about eight steps. A literal of fewer than 64 digits
    /// pays the one step it always did.
    /// </summary>
    private const int BINARY_WORD_STEPS = 8;

    private const int HEXADECIMAL_DIGIT_BITS = 4;
    private const int PREFIX_LENGTH = 2;
    private const char DOT = '.';
    private const char ZERO = '0';

    #endregion

    #region Functions

    /// <summary>
    /// The value of a numeral.
    /// </summary>
    /// <param name="text">The numeral's token.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="SmtPartialException">The token is no numeral, or the number is past a budget.</exception>
    public static SmtValue Numeral(string text, SmtMeter meter)
    {
        return Digits(text, meter);
    }

    /// <summary>
    /// The integer a run of decimal digits writes.
    /// </summary>
    /// <param name="digits">The digits, at least one.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <param name="leadingZeros">Whether the digits may start with zeros: a numeral may not, the content of a string may.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="SmtPartialException">The text is not digits, or the number is past a budget.</exception>
    public static SmtIntValue Digits(ReadOnlySpan<char> digits, SmtMeter meter, bool leadingZeros = false)
    {
        return new SmtIntValue(Parse(digits, meter, leadingZeros));
    }

    /// <summary>
    /// The value of a decimal: exactly the rational its digits write.
    /// </summary>
    /// <param name="text">The decimal's token.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The Real.</returns>
    /// <exception cref="SmtPartialException">The token is no decimal, or the number is past a budget.</exception>
    public static SmtValue Decimal(string text, SmtMeter meter)
    {
        var dot = text.IndexOf(DOT);
        if (dot <= 0 || dot == text.Length - 1)
            throw meter.Doubt();

        var fraction = text.Length - dot - 1;
        var digits = string.Concat(text.AsSpan(0, dot), text.AsSpan(dot + 1));

        // The numerator and ten to the number of digits after the dot are both about as long
        // as the digits; reducing the fraction costs about the square of that.
        var bits = BitsOf(digits.Length);
        meter.CheckIntegerBits(bits);
        meter.ChargeProduct(2 * bits, 2 * bits);
        var value = SmtRational.OfDecimal(Parse(digits, meter, true), fraction);
        meter.CheckIntegerBits(value.Bits);
        return new SmtRealValue(value);
    }

    /// <summary>
    /// The value of a hexadecimal literal: four bits a digit.
    /// </summary>
    /// <param name="text">The literal's token, <c>#x</c> first.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The bit-vector.</returns>
    /// <exception cref="SmtPartialException">The token is no such literal, or the width is past a budget.</exception>
    public static SmtValue Hexadecimal(string text, SmtMeter meter)
    {
        if (text.Length <= PREFIX_LENGTH || text[0] != '#' || text[1] != 'x')
            throw meter.Doubt();

        var digits = text.AsSpan(PREFIX_LENGTH);
        foreach (var digit in digits)
        {
            if (!char.IsAsciiHexDigit(digit))
                throw meter.Doubt();
        }

        var width = (long)digits.Length * HEXADECIMAL_DIGIT_BITS;
        meter.CheckBitVectorBits(width);
        meter.ChargeBits(width);

        // A leading zero keeps the first digit from being read as a sign.
        var value = BigInteger.Parse(string.Concat("0", digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return new SmtBitVectorValue(value, (int)width);
    }

    /// <summary>
    /// The value of a binary literal: one bit a digit.
    /// </summary>
    /// <param name="text">The literal's token, <c>#b</c> first.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The bit-vector.</returns>
    /// <exception cref="SmtPartialException">The token is no such literal, or the width is past a budget.</exception>
    public static SmtValue Binary(string text, SmtMeter meter)
    {
        if (text.Length <= PREFIX_LENGTH || text[0] != '#' || text[1] != 'b')
            throw meter.Doubt();

        var digits = text.AsSpan(PREFIX_LENGTH);
        foreach (var digit in digits)
        {
            if (digit is not ('0' or '1'))
                throw meter.Doubt();
        }

        meter.CheckBitVectorBits(digits.Length);
        meter.Charge(1 + (long)(digits.Length / SmtMeter.WORD_BITS) * BINARY_WORD_STEPS);
        var value = BigInteger.Parse(string.Concat("0", digits), NumberStyles.AllowBinarySpecifier, CultureInfo.InvariantCulture);
        return new SmtBitVectorValue(value, digits.Length);
    }

    /// <summary>
    /// The value of <c>(_ bvN w)</c>: the bit-vector of a width whose unsigned value is N.
    /// </summary>
    /// <param name="digits">The digits of N.</param>
    /// <param name="width">The width.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The bit-vector.</returns>
    /// <exception cref="SmtPartialException">N does not fit the width, or the width is past a budget.</exception>
    public static SmtValue BitVector(ReadOnlySpan<char> digits, int width, SmtMeter meter)
    {
        if (width < 1)
            throw meter.Doubt();

        meter.CheckBitVectorBits(width);
        meter.ChargeBits(width);

        // A number that does not fit the width is written by no solver, and read by them in more than one way.
        var value = Parse(digits, meter, false);
        return value.GetBitLength() <= width ? new SmtBitVectorValue(value, width) : throw meter.Doubt();
    }

    /// <summary>
    /// The value of a string literal: its characters, the standard's escapes replaced
    /// (<see cref="SmtStringLiteral"/>).
    /// </summary>
    /// <param name="text">The literal's content.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The string.</returns>
    /// <exception cref="SmtPartialException">The literal is one readers read differently, or is past a budget.</exception>
    public static SmtValue String(string text, SmtMeter meter)
    {
        meter.CheckStringCharacters(text.Length);
        meter.Charge(1 + text.Length);
        return SmtStringLiteral.TryDecode(text, out var codePoints)
            ? new SmtStringValue(codePoints)
            : throw meter.Undecided(SmtValidationReasons.STRING_LITERAL);
    }

    /// <summary>
    /// Reads decimal digits as an integer. A numeral has no leading zero; the digits of a
    /// decimal, or of a string turned into a number, may.
    /// </summary>
    private static BigInteger Parse(ReadOnlySpan<char> digits, SmtMeter meter, bool leadingZeros)
    {
        if (digits.IsEmpty || (!leadingZeros && digits.Length > 1 && digits[0] == ZERO))
            throw meter.Doubt();

        meter.Charge(1 + digits.Length / WORD_DIGITS);
        foreach (var digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
                throw meter.Doubt();
        }

        digits = digits.TrimStart(ZERO);
        if (digits.IsEmpty)
            return BigInteger.Zero;

        if (digits.Length <= WORD_DIGITS)
            return long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

        // Reading n digits costs about the square of n; the number has about 3.3 bits a digit.
        var bits = BitsOf(digits.Length);
        meter.CheckIntegerBits(bits);
        meter.ChargeProduct(bits, bits);
        var value = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        meter.CheckIntegerBits(value.GetBitLength());
        return value;
    }

    /// <summary>
    /// A lower bound of the bits of a number of so many digits whose first is not zero: a
    /// number refused by it is past the ceiling for certain.
    /// </summary>
    private static long BitsOf(int digits)
    {
        return (long)(digits - 1) * 3;
    }

    #endregion
}
