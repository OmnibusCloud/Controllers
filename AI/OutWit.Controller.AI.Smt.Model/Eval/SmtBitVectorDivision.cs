using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The division operators of bit-vectors, as the standard defines them. The unsigned ones are
/// the base: <c>bvudiv</c> by zero is all ones, <c>bvurem</c> by zero is the dividend. The
/// signed ones are the standard's own definitions over those two, written out case by case on
/// the signs of the operands, so that their values at zero and at the most negative number are
/// the standard's and not a rule of thumb:
/// <list type="bullet">
/// <item><c>bvsdiv</c> truncates toward zero;</item>
/// <item><c>bvsrem</c> has the sign of the dividend;</item>
/// <item><c>bvsmod</c> has the sign of the divisor.</item>
/// </list>
/// </summary>
internal static class SmtBitVectorDivision
{
    #region Functions

    /// <summary>
    /// Applies a division operator to two bit-vectors of one width.
    /// </summary>
    /// <param name="known">The operator: one of the five divisions.</param>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The operator is no division.</exception>
    public static SmtBitVectorValue Apply(SmtOperator known, SmtBitVectorValue left, SmtBitVectorValue right)
    {
        return known switch
        {
            SmtOperator.BvUdiv => UnsignedDivide(left, right),
            SmtOperator.BvUrem => UnsignedRemainder(left, right),
            SmtOperator.BvSdiv => SignedDivide(left, right),
            SmtOperator.BvSrem => SignedRemainder(left, right),
            SmtOperator.BvSmod => SignedModulo(left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(known))
        };
    }

    private static SmtBitVectorValue UnsignedDivide(SmtBitVectorValue left, SmtBitVectorValue right)
    {
        return new SmtBitVectorValue(right.Value.IsZero ? left.Mask : left.Value / right.Value, left.Width);
    }

    private static SmtBitVectorValue UnsignedRemainder(SmtBitVectorValue left, SmtBitVectorValue right)
    {
        return right.Value.IsZero ? left : new SmtBitVectorValue(left.Value % right.Value, left.Width);
    }

    private static SmtBitVectorValue Negate(SmtBitVectorValue value)
    {
        return SmtBitVectorValue.Wrap(-value.Value, value.Width);
    }

    private static SmtBitVectorValue SignedDivide(SmtBitVectorValue left, SmtBitVectorValue right)
    {
        return (left.IsNegative, right.IsNegative) switch
        {
            (false, false) => UnsignedDivide(left, right),
            (true, false) => Negate(UnsignedDivide(Negate(left), right)),
            (false, true) => Negate(UnsignedDivide(left, Negate(right))),
            _ => UnsignedDivide(Negate(left), Negate(right))
        };
    }

    private static SmtBitVectorValue SignedRemainder(SmtBitVectorValue left, SmtBitVectorValue right)
    {
        return (left.IsNegative, right.IsNegative) switch
        {
            (false, false) => UnsignedRemainder(left, right),
            (true, false) => Negate(UnsignedRemainder(Negate(left), right)),
            (false, true) => UnsignedRemainder(left, Negate(right)),
            _ => Negate(UnsignedRemainder(Negate(left), Negate(right)))
        };
    }

    private static SmtBitVectorValue SignedModulo(SmtBitVectorValue left, SmtBitVectorValue right)
    {
        var magnitude = UnsignedRemainder(left.IsNegative ? Negate(left) : left, right.IsNegative ? Negate(right) : right);
        if (magnitude.Value.IsZero)
            return magnitude;

        return (left.IsNegative, right.IsNegative) switch
        {
            (false, false) => magnitude,
            (true, false) => SmtBitVectorValue.Wrap(right.Value - magnitude.Value, left.Width),
            (false, true) => SmtBitVectorValue.Wrap(magnitude.Value + right.Value, left.Width),
            _ => Negate(magnitude)
        };
    }

    #endregion
}
