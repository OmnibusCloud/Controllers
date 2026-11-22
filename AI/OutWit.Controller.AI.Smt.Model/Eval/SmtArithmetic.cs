using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The operators of Ints and Reals, exact: integers of any size and rationals in lowest terms.
/// Integers stay integers under <c>+</c>, <c>-</c> and <c>*</c>; one Real among the arguments
/// makes the result a Real, as it does for solvers that mix the two sorts; <c>/</c> is always a
/// Real. <c>div</c> and <c>mod</c> are the standard's: for a divisor n that is not zero,
/// m = n * (div m n) + (mod m n) with 0 &lt;= (mod m n) &lt; |n|, so the remainder is never
/// negative and the quotient rounds toward the side the divisor's sign sets. Division by zero
/// is left to the model by the standard, and is not evaluated.
/// </summary>
internal static class SmtArithmetic
{
    #region Functions

    /// <summary>
    /// Applies an arithmetic operator.
    /// </summary>
    /// <param name="known">The operator.</param>
    /// <param name="arguments">The values of the arguments, at least one.</param>
    /// <param name="indices">The index of <c>divisible</c>; none otherwise.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application does not fit the operator, divides by zero, or is past a budget.</exception>
    public static SmtValue Apply(SmtOperator known, SmtValue[] arguments, int[] indices, SmtMeter meter)
    {
        foreach (var argument in arguments)
        {
            if (argument.Kind is not (SmtValueKind.Int or SmtValueKind.Real))
                throw meter.Doubt();
        }

        switch (known)
        {
            case SmtOperator.Add:
            case SmtOperator.Multiply:
                return Fold(known, arguments, meter);
            case SmtOperator.Subtract:
                return arguments.Length == 1 ? Negate(arguments[0]) : Fold(known, arguments, meter);
            case SmtOperator.Divide when arguments.Length >= 2:
                return Divide(arguments, meter);
            case SmtOperator.IntegerDivide when arguments.Length >= 2:
                return IntegerDivide(arguments, meter);
            case SmtOperator.Modulo when arguments.Length == 2:
                return new SmtIntValue(FloorDivide(Integer(arguments[0], meter), Integer(arguments[1], meter), meter).Remainder);
            case SmtOperator.Absolute when arguments.Length == 1:
                return new SmtIntValue(BigInteger.Abs(Integer(arguments[0], meter)));
            case SmtOperator.Less when arguments.Length >= 2:
                return Chain(arguments, meter, order => order < 0);
            case SmtOperator.LessOrEqual when arguments.Length >= 2:
                return Chain(arguments, meter, order => order <= 0);
            case SmtOperator.Greater when arguments.Length >= 2:
                return Chain(arguments, meter, order => order > 0);
            case SmtOperator.GreaterOrEqual when arguments.Length >= 2:
                return Chain(arguments, meter, order => order >= 0);
            case SmtOperator.ToReal when arguments.Length == 1:
                return new SmtRealValue(new SmtRational(Integer(arguments[0], meter)));
            case SmtOperator.ToInt when arguments.Length == 1:
                meter.ChargeProduct(arguments[0].Weight * SmtMeter.WORD_BITS, arguments[0].Weight * SmtMeter.WORD_BITS);
                return arguments[0] is SmtRealValue real ? new SmtIntValue(real.Value.Floor()) : arguments[0];
            case SmtOperator.IsInt when arguments.Length == 1:
                return SmtBoolValue.Of(SmtValueComparer.Rational(arguments[0]).IsInteger);
            case SmtOperator.Divisible when arguments.Length == 1 && indices.Length == 1 && indices[0] >= 1:
                return SmtBoolValue.Of(FloorDivide(Integer(arguments[0], meter), indices[0], meter).Remainder.IsZero);
            default:
                throw meter.Doubt();
        }
    }

    /// <summary>
    /// Compares two numbers, each an Int or a Real, and pays for it: two integers cost their
    /// sizes, anything with a rational the product of the sizes, since rationals are compared
    /// by multiplying across.
    /// </summary>
    /// <param name="left">One number.</param>
    /// <param name="right">The other.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>Negative, zero or positive.</returns>
    /// <exception cref="SmtPartialException">The step budget is used up.</exception>
    public static int Compare(SmtValue left, SmtValue right, SmtMeter meter)
    {
        if (left.Kind == SmtValueKind.Int && right.Kind == SmtValueKind.Int)
            meter.Charge(left.Weight + right.Weight);
        else
            meter.Charge(left.Weight > long.MaxValue / right.Weight ? long.MaxValue : left.Weight * right.Weight);

        return SmtValueComparer.CompareNumbers(left, right);
    }

    /// <summary>
    /// Divides an integer by another that is not zero, the standard's way.
    /// </summary>
    /// <param name="dividend">The dividend.</param>
    /// <param name="divisor">The divisor.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The quotient and the remainder, the remainder never negative.</returns>
    /// <exception cref="SmtPartialException">The divisor is zero, or the step budget is used up.</exception>
    public static (BigInteger Quotient, BigInteger Remainder) FloorDivide(BigInteger dividend, BigInteger divisor, SmtMeter meter)
    {
        if (divisor.IsZero)
            throw meter.Undecided(SmtValidationReasons.DIVISION_BY_ZERO);

        meter.ChargeProduct(dividend.GetBitLength(), divisor.GetBitLength());
        var quotient = BigInteger.DivRem(dividend, divisor, out var remainder);
        if (remainder.Sign < 0)
        {
            // The remainder of the machine's division has the dividend's sign: move it up by
            // one divisor, and the quotient the other way.
            remainder += BigInteger.Abs(divisor);
            quotient = divisor.Sign > 0 ? quotient - BigInteger.One : quotient + BigInteger.One;
        }

        return (quotient, remainder);
    }

    private static SmtValue Fold(SmtOperator known, SmtValue[] arguments, SmtMeter meter)
    {
        var result = arguments[0];
        for (var index = 1; index < arguments.Length; index++)
            result = Combine(known, result, arguments[index], meter);

        return result;
    }

    private static SmtValue Combine(SmtOperator known, SmtValue left, SmtValue right, SmtMeter meter)
    {
        if (left is SmtIntValue leftWhole && right is SmtIntValue rightWhole)
            return new SmtIntValue(Combine(known, leftWhole.Value, rightWhole.Value, meter));

        var first = SmtValueComparer.Rational(left);
        var second = SmtValueComparer.Rational(right);
        PayForRationals(first, second, meter);
        return new SmtRealValue(known switch
        {
            SmtOperator.Add => first + second,
            SmtOperator.Subtract => first - second,
            _ => first * second
        });
    }

    private static BigInteger Combine(SmtOperator known, BigInteger left, BigInteger right, SmtMeter meter)
    {
        var leftBits = left.GetBitLength();
        var rightBits = right.GetBitLength();
        if (known == SmtOperator.Multiply)
        {
            meter.CheckIntegerBits(leftBits + rightBits);
            meter.ChargeProduct(leftBits, rightBits);
            return left * right;
        }

        meter.CheckIntegerBits(Math.Max(leftBits, rightBits) + 1);
        meter.ChargeBits(leftBits + rightBits);
        return known == SmtOperator.Add ? left + right : left - right;
    }

    private static SmtValue Negate(SmtValue value)
    {
        return value is SmtIntValue whole ? new SmtIntValue(-whole.Value) : new SmtRealValue(-((SmtRealValue)value).Value);
    }

    private static SmtValue Divide(SmtValue[] arguments, SmtMeter meter)
    {
        var result = SmtValueComparer.Rational(arguments[0]);
        for (var index = 1; index < arguments.Length; index++)
        {
            var divisor = SmtValueComparer.Rational(arguments[index]);
            if (divisor.Sign == 0)
                throw meter.Undecided(SmtValidationReasons.DIVISION_BY_ZERO);

            PayForRationals(result, divisor, meter);
            result /= divisor;
        }

        return new SmtRealValue(result);
    }

    private static SmtValue IntegerDivide(SmtValue[] arguments, SmtMeter meter)
    {
        var result = Integer(arguments[0], meter);
        for (var index = 1; index < arguments.Length; index++)
            result = FloorDivide(result, Integer(arguments[index], meter), meter).Quotient;

        return new SmtIntValue(result);
    }

    private static SmtValue Chain(SmtValue[] arguments, SmtMeter meter, Func<int, bool> holds)
    {
        for (var index = 1; index < arguments.Length; index++)
        {
            if (!holds(Compare(arguments[index - 1], arguments[index], meter)))
                return SmtBoolValue.FALSE;
        }

        return SmtBoolValue.TRUE;
    }

    /// <summary>
    /// An argument of <c>div</c>, <c>mod</c>, <c>abs</c> or <c>to_real</c> is of sort Int. A
    /// Real there, even a whole one, is a term the standard does not have, and one a solver
    /// that takes it converts by a rule of its own.
    /// </summary>
    private static BigInteger Integer(SmtValue value, SmtMeter meter)
    {
        return value is SmtIntValue whole ? whole.Value : throw meter.Doubt();
    }

    /// <summary>
    /// Checks and pays for one operation on two rationals: the result has no more bits than
    /// the two together, and the products and the reduction to lowest terms cost about the
    /// square of that.
    /// </summary>
    private static void PayForRationals(SmtRational left, SmtRational right, SmtMeter meter)
    {
        var bits = left.Bits + right.Bits;
        meter.CheckIntegerBits(bits + 1);
        meter.ChargeProduct(bits, bits);
    }

    #endregion
}
