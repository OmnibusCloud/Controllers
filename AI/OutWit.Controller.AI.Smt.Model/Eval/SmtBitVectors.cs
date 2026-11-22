using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The operators of fixed-size bit-vectors (the logic QF_BV), on unsigned values below two to
/// the width. Division by zero is defined by the standard here, not left to the model:
/// <c>bvudiv</c> by zero is all ones and <c>bvurem</c> by zero is the dividend, and the signed
/// operators follow from those two by the standard's own definitions.
/// </summary>
internal static class SmtBitVectors
{
    #region Functions

    /// <summary>
    /// Applies a bit-vector operator without indices.
    /// </summary>
    /// <param name="known">The operator.</param>
    /// <param name="arguments">The values of the arguments, at least one.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application does not fit the operator, or is past a budget.</exception>
    public static SmtValue Apply(SmtOperator known, SmtValue[] arguments, SmtMeter meter)
    {
        var vectors = new SmtBitVectorValue[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
            vectors[index] = arguments[index] as SmtBitVectorValue ?? throw meter.Doubt();

        var first = vectors[0];
        if (known == SmtOperator.Concat)
            return vectors.Length >= 2 ? Concat(vectors, meter) : throw meter.Doubt();

        // Every other operator takes arguments of one width.
        foreach (var vector in vectors)
        {
            if (vector.Width != first.Width)
                throw meter.Doubt();
        }

        meter.ChargeBits((long)first.Width * vectors.Length);
        switch (known)
        {
            case SmtOperator.BvNot when vectors.Length == 1:
                return new SmtBitVectorValue(first.Value ^ first.Mask, first.Width);
            case SmtOperator.BvNeg when vectors.Length == 1:
                return SmtBitVectorValue.Wrap(-first.Value, first.Width);
            case SmtOperator.BvToNat when vectors.Length == 1:
                return new SmtIntValue(first.Value);
            case SmtOperator.BvToInt when vectors.Length == 1:
                return new SmtIntValue(first.Signed);
            case SmtOperator.BvAnd when vectors.Length >= 2:
                return Fold(vectors, (left, right) => left & right);
            case SmtOperator.BvOr when vectors.Length >= 2:
                return Fold(vectors, (left, right) => left | right);
            case SmtOperator.BvXor when vectors.Length >= 2:
                return Fold(vectors, (left, right) => left ^ right);
            case SmtOperator.BvAdd when vectors.Length >= 2:
                return Fold(vectors, (left, right) => left + right);
            case SmtOperator.BvMul when vectors.Length >= 2:
                meter.ChargeProduct((long)first.Width * vectors.Length, first.Width);
                return Fold(vectors, (left, right) => left * right);
        }

        if (vectors.Length != 2)
            throw meter.Doubt();

        return Binary(known, first, vectors[1], meter);
    }

    private static SmtValue Binary(SmtOperator known, SmtBitVectorValue left, SmtBitVectorValue right, SmtMeter meter)
    {
        var width = left.Width;
        switch (known)
        {
            case SmtOperator.BvSub:
                return SmtBitVectorValue.Wrap(left.Value - right.Value, width);
            case SmtOperator.BvNand:
                return new SmtBitVectorValue((left.Value & right.Value) ^ left.Mask, width);
            case SmtOperator.BvNor:
                return new SmtBitVectorValue((left.Value | right.Value) ^ left.Mask, width);
            case SmtOperator.BvXnor:
                return new SmtBitVectorValue(left.Value ^ right.Value ^ left.Mask, width);
            case SmtOperator.BvComp:
                return new SmtBitVectorValue(left.Value == right.Value ? BigInteger.One : BigInteger.Zero, 1);
            case SmtOperator.BvShl:
                return right.Value >= width ? Zero(width) : SmtBitVectorValue.Wrap(left.Value << (int)right.Value, width);
            case SmtOperator.BvLshr:
                return right.Value >= width ? Zero(width) : new SmtBitVectorValue(left.Value >> (int)right.Value, width);
            case SmtOperator.BvAshr:
                // The sign bit fills from the left: shifting the two's-complement number does that.
                return right.Value >= width
                    ? new SmtBitVectorValue(left.IsNegative ? left.Mask : BigInteger.Zero, width)
                    : SmtBitVectorValue.Wrap(left.Signed >> (int)right.Value, width);
            case SmtOperator.BvUlt:
                return SmtBoolValue.Of(left.Value < right.Value);
            case SmtOperator.BvUle:
                return SmtBoolValue.Of(left.Value <= right.Value);
            case SmtOperator.BvUgt:
                return SmtBoolValue.Of(left.Value > right.Value);
            case SmtOperator.BvUge:
                return SmtBoolValue.Of(left.Value >= right.Value);
            case SmtOperator.BvSlt:
                return SmtBoolValue.Of(left.Signed < right.Signed);
            case SmtOperator.BvSle:
                return SmtBoolValue.Of(left.Signed <= right.Signed);
            case SmtOperator.BvSgt:
                return SmtBoolValue.Of(left.Signed > right.Signed);
            case SmtOperator.BvSge:
                return SmtBoolValue.Of(left.Signed >= right.Signed);
            case SmtOperator.BvUdiv:
            case SmtOperator.BvUrem:
            case SmtOperator.BvSdiv:
            case SmtOperator.BvSrem:
            case SmtOperator.BvSmod:
                meter.ChargeProduct(width, width);
                return SmtBitVectorDivision.Apply(known, left, right);
            default:
                throw meter.Doubt();
        }
    }

    private static SmtValue Concat(SmtBitVectorValue[] vectors, SmtMeter meter)
    {
        long width = 0;
        foreach (var vector in vectors)
            width += vector.Width;

        // The first argument is the most significant part.
        meter.CheckBitVectorBits(width);
        meter.ChargeBits(width * vectors.Length);
        var value = BigInteger.Zero;
        foreach (var vector in vectors)
            value = (value << vector.Width) | vector.Value;

        return new SmtBitVectorValue(value, (int)width);
    }

    private static SmtValue Fold(SmtBitVectorValue[] vectors, Func<BigInteger, BigInteger, BigInteger> combine)
    {
        // Reduced after every step: a product of many factors never grows past two widths.
        var mask = vectors[0].Mask;
        var value = vectors[0].Value;
        for (var index = 1; index < vectors.Length; index++)
            value = combine(value, vectors[index].Value) & mask;

        return new SmtBitVectorValue(value, vectors[0].Width);
    }

    private static SmtBitVectorValue Zero(int width)
    {
        return new SmtBitVectorValue(BigInteger.Zero, width);
    }

    #endregion
}
