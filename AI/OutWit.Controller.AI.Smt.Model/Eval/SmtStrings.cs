using System.Globalization;
using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The basic operators of the theory of strings, as the standard defines them at their edges:
/// a position outside the string gives the empty string or -1, never an error. Positions and
/// lengths count code points. Regular expressions are not evaluated.
/// </summary>
internal static class SmtStrings
{
    #region Constants

    /// <summary>The largest code point of the theory's characters.</summary>
    public const int MAX_CODE_POINT = 0x2FFFF;

    private const int NOT_FOUND = -1;

    #endregion

    #region Functions

    /// <summary>
    /// Applies a string operator.
    /// </summary>
    /// <param name="known">The operator.</param>
    /// <param name="arguments">The values of the arguments, at least one.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application does not fit the operator, or is past a budget.</exception>
    public static SmtValue Apply(SmtOperator known, SmtValue[] arguments, SmtMeter meter)
    {
        switch (known)
        {
            case SmtOperator.StrConcat when arguments.Length >= 2:
                return Concat(arguments, meter);
            case SmtOperator.StrLen when arguments.Length == 1:
                return Number(Text(arguments[0], meter).Length);
            case SmtOperator.StrAt when arguments.Length == 2:
                return Substring(Text(arguments[0], meter), Whole(arguments[1], meter), BigInteger.One, meter);
            case SmtOperator.StrSubstr when arguments.Length == 3:
                return Substring(Text(arguments[0], meter), Whole(arguments[1], meter), Whole(arguments[2], meter), meter);
            case SmtOperator.StrPrefixOf when arguments.Length == 2:
                return SmtBoolValue.Of(StartsAt(Text(arguments[1], meter), Text(arguments[0], meter), 0, meter));
            case SmtOperator.StrSuffixOf when arguments.Length == 2:
                var suffix = Text(arguments[0], meter);
                var whole = Text(arguments[1], meter);
                return SmtBoolValue.Of(StartsAt(whole, suffix, whole.Length - suffix.Length, meter));
            case SmtOperator.StrContains when arguments.Length == 2:
                return SmtBoolValue.Of(Find(Text(arguments[0], meter), Text(arguments[1], meter), 0, meter) != NOT_FOUND);
            case SmtOperator.StrIndexOf when arguments.Length == 3:
                return IndexOf(Text(arguments[0], meter), Text(arguments[1], meter), Whole(arguments[2], meter), meter);
            case SmtOperator.StrReplace when arguments.Length == 3:
                return Replace(Text(arguments[0], meter), Text(arguments[1], meter), Text(arguments[2], meter), meter);
            case SmtOperator.StrLess when arguments.Length == 2:
                return SmtBoolValue.Of(Order(arguments, meter) < 0);
            case SmtOperator.StrLessOrEqual when arguments.Length == 2:
                return SmtBoolValue.Of(Order(arguments, meter) <= 0);
            case SmtOperator.StrToInt when arguments.Length == 1:
                return ToInteger(Text(arguments[0], meter), meter);
            case SmtOperator.StrFromInt when arguments.Length == 1:
                return OfInteger(Whole(arguments[0], meter), meter);
            case SmtOperator.StrIsDigit when arguments.Length == 1:
                var digit = Text(arguments[0], meter);
                return SmtBoolValue.Of(digit.Length == 1 && IsDigit(digit.CodePoints[0]));
            case SmtOperator.StrToCode when arguments.Length == 1:
                var character = Text(arguments[0], meter);
                return Number(character.Length == 1 ? character.CodePoints[0] : NOT_FOUND);
            case SmtOperator.StrFromCode when arguments.Length == 1:
                var code = Whole(arguments[0], meter);
                return code.Sign >= 0 && code <= MAX_CODE_POINT ? new SmtStringValue([(int)code]) : SmtStringValue.EMPTY;
            default:
                throw meter.Doubt();
        }
    }

    private static SmtStringValue Text(SmtValue value, SmtMeter meter)
    {
        return value as SmtStringValue ?? throw meter.Doubt();
    }

    private static BigInteger Whole(SmtValue value, SmtMeter meter)
    {
        return value is SmtIntValue integer ? integer.Value : throw meter.Doubt();
    }

    private static SmtIntValue Number(int value)
    {
        return new SmtIntValue(value);
    }

    private static bool IsDigit(int codePoint)
    {
        return codePoint is >= '0' and <= '9';
    }

    private static SmtValue Concat(SmtValue[] arguments, SmtMeter meter)
    {
        long length = 0;
        foreach (var argument in arguments)
            length += Text(argument, meter).Length;

        meter.CheckStringCharacters(length);
        meter.Charge(1 + length);
        var codePoints = new int[length];
        var position = 0;
        foreach (var argument in arguments)
        {
            var part = ((SmtStringValue)argument).CodePoints;
            part.CopyTo(codePoints, position);
            position += part.Length;
        }

        return new SmtStringValue(codePoints);
    }

    /// <summary>
    /// <c>str.substr</c>: from a position inside the string, at most as many characters as
    /// asked for and as are left; the empty string for a position outside or a length below one.
    /// </summary>
    private static SmtValue Substring(SmtStringValue text, BigInteger start, BigInteger length, SmtMeter meter)
    {
        if (start.Sign < 0 || length.Sign <= 0 || start >= text.Length)
            return SmtStringValue.EMPTY;

        var from = (int)start;
        var count = (int)BigInteger.Min(length, text.Length - from);
        meter.Charge(1 + count);
        return new SmtStringValue(text.CodePoints.AsSpan(from, count).ToArray());
    }

    private static bool StartsAt(SmtStringValue text, SmtStringValue part, int position, SmtMeter meter)
    {
        if (position < 0 || position > text.Length - part.Length)
            return false;

        meter.Charge(1 + part.Length);
        return text.CodePoints.AsSpan(position, part.Length).SequenceEqual(part.CodePoints);
    }

    /// <summary>
    /// The first position at or after a start where a part stands in a text; the empty part
    /// stands everywhere. Paid as the plain search it is: every position against the whole part.
    /// </summary>
    private static int Find(SmtStringValue text, SmtStringValue part, int start, SmtMeter meter)
    {
        var positions = (long)text.Length - start - part.Length + 1;
        if (positions <= 0)
            return NOT_FOUND;

        meter.Charge(positions > long.MaxValue / (1 + part.Length) ? long.MaxValue : positions * (1 + part.Length));
        var found = text.CodePoints.AsSpan(start).IndexOf(part.CodePoints);
        return found < 0 ? NOT_FOUND : found + start;
    }

    /// <summary>
    /// <c>str.indexof</c>: -1 for a start outside the string, the start itself for the empty part.
    /// </summary>
    private static SmtValue IndexOf(SmtStringValue text, SmtStringValue part, BigInteger start, SmtMeter meter)
    {
        if (start.Sign < 0 || start > text.Length)
            return Number(NOT_FOUND);

        return Number(Find(text, part, (int)start, meter));
    }

    /// <summary>
    /// <c>str.replace</c>: the first occurrence replaced; the empty part is found at the start,
    /// so the replacement is put in front.
    /// </summary>
    private static SmtValue Replace(SmtStringValue text, SmtStringValue part, SmtStringValue replacement, SmtMeter meter)
    {
        var position = Find(text, part, 0, meter);
        if (position == NOT_FOUND)
            return text;

        var length = (long)text.Length - part.Length + replacement.Length;
        meter.CheckStringCharacters(length);
        meter.Charge(1 + length);
        var codePoints = new int[length];
        text.CodePoints.AsSpan(0, position).CopyTo(codePoints);
        replacement.CodePoints.CopyTo(codePoints, position);
        text.CodePoints.AsSpan(position + part.Length).CopyTo(codePoints.AsSpan(position + replacement.Length));
        return new SmtStringValue(codePoints);
    }

    private static int Order(SmtValue[] arguments, SmtMeter meter)
    {
        var left = Text(arguments[0], meter);
        var right = Text(arguments[1], meter);
        meter.Charge(1 + Math.Min(left.Length, right.Length));
        return left.CodePoints.AsSpan().SequenceCompareTo(right.CodePoints);
    }

    /// <summary>
    /// <c>str.to_int</c>: the number a string of decimal digits writes; -1 for the empty
    /// string and for a string with anything else in it.
    /// </summary>
    private static SmtValue ToInteger(SmtStringValue text, SmtMeter meter)
    {
        if (text.Length == 0)
            return Number(NOT_FOUND);

        // Every character is looked at, and copied, before the digits are read as a number, and
        // a string that is no number is looked at for nothing: the look is paid for as reading
        // the digits of a numeral is, before it is made.
        meter.Charge(text.Length / SmtLiterals.WORD_DIGITS);
        var digits = new char[text.Length];
        for (var index = 0; index < digits.Length; index++)
        {
            if (!IsDigit(text.CodePoints[index]))
                return Number(NOT_FOUND);

            digits[index] = (char)text.CodePoints[index];
        }

        return SmtLiterals.Digits(digits, meter, true);
    }

    /// <summary>
    /// <c>str.from_int</c>: the decimal digits of a number that is not negative; the empty
    /// string for a negative one.
    /// </summary>
    private static SmtValue OfInteger(BigInteger value, SmtMeter meter)
    {
        if (value.Sign < 0)
            return SmtStringValue.EMPTY;

        var bits = value.GetBitLength();
        meter.CheckStringCharacters(bits / 4);
        meter.ChargeProduct(bits, bits);
        var digits = value.ToString(CultureInfo.InvariantCulture);
        meter.CheckStringCharacters(digits.Length);

        var codePoints = new int[digits.Length];
        for (var index = 0; index < codePoints.Length; index++)
            codePoints[index] = digits[index];

        return new SmtStringValue(codePoints);
    }

    #endregion
}
