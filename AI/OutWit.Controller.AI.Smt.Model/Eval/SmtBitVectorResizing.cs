using System.Numerics;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The indexed operators of bit-vectors: the ones whose result has another width
/// (<c>extract</c>, <c>zero_extend</c>, <c>sign_extend</c>, <c>repeat</c>, the conversion of an
/// integer) and the rotations. A width that would pass the ceiling is refused before any bit
/// of it is made.
/// </summary>
internal static class SmtBitVectorResizing
{
    #region Functions

    /// <summary>
    /// Applies an indexed bit-vector operator.
    /// </summary>
    /// <param name="known">The operator.</param>
    /// <param name="arguments">The values of the arguments.</param>
    /// <param name="indices">The operator's indices.</param>
    /// <param name="meter">The validation's accounting.</param>
    /// <returns>The value.</returns>
    /// <exception cref="SmtPartialException">The application does not fit the operator, or is past a budget.</exception>
    public static SmtValue Apply(SmtOperator known, SmtValue[] arguments, int[] indices, SmtMeter meter)
    {
        if (arguments.Length != 1)
            throw meter.Doubt();

        if (known == SmtOperator.IntToBv)
            return OfInteger(arguments[0], indices, meter);

        if (arguments[0] is not SmtBitVectorValue vector)
            throw meter.Doubt();

        var width = vector.Width;
        switch (known)
        {
            case SmtOperator.Extract when indices.Length == 2 && indices[0] < width && indices[1] <= indices[0]:
                meter.ChargeBits(width);
                var bits = indices[0] - indices[1] + 1;
                return new SmtBitVectorValue((vector.Value >> indices[1]) & ((BigInteger.One << bits) - BigInteger.One), bits);
            case SmtOperator.ZeroExtend when indices.Length == 1:
                Pay((long)width + indices[0], meter);
                return new SmtBitVectorValue(vector.Value, width + indices[0]);
            case SmtOperator.SignExtend when indices.Length == 1:
                Pay((long)width + indices[0], meter);
                return SmtBitVectorValue.Wrap(vector.Signed, width + indices[0]);
            case SmtOperator.Repeat when indices.Length == 1 && indices[0] >= 1:
                Pay((long)width * indices[0], meter);
                return Repeat(vector, indices[0]);
            case SmtOperator.RotateLeft when indices.Length == 1:
                meter.ChargeBits(width);
                return Rotate(vector, indices[0] % width);
            case SmtOperator.RotateRight when indices.Length == 1:
                meter.ChargeBits(width);
                return Rotate(vector, (width - indices[0] % width) % width);
            default:
                throw meter.Doubt();
        }
    }

    /// <summary>
    /// <c>((_ int2bv w) n)</c>: the integer modulo two to the width, so a negative integer
    /// gives its two's complement.
    /// </summary>
    private static SmtValue OfInteger(SmtValue value, int[] indices, SmtMeter meter)
    {
        if (value is not SmtIntValue integer || indices.Length != 1 || indices[0] < 1)
            throw meter.Doubt();

        meter.CheckBitVectorBits(indices[0]);
        meter.ChargeBits(integer.Value.GetBitLength() + indices[0]);
        return SmtBitVectorValue.Wrap(integer.Value, indices[0]);
    }

    private static void Pay(long width, SmtMeter meter)
    {
        meter.CheckBitVectorBits(width);
        meter.ChargeBits(width);
    }

    /// <summary>
    /// The vector written a number of times in a row, built by doubling: the cost is the size
    /// of the result, not the count times the size.
    /// </summary>
    private static SmtValue Repeat(SmtBitVectorValue vector, int count)
    {
        var result = BigInteger.Zero;
        var resultWidth = 0;
        var block = vector.Value;
        var blockWidth = vector.Width;
        for (var remaining = count; remaining > 0; remaining >>= 1)
        {
            if ((remaining & 1) != 0)
            {
                result = (result << blockWidth) | block;
                resultWidth += blockWidth;
            }

            if (remaining > 1)
            {
                block = (block << blockWidth) | block;
                blockWidth *= 2;
            }
        }

        return new SmtBitVectorValue(result, resultWidth);
    }

    private static SmtValue Rotate(SmtBitVectorValue vector, int left)
    {
        if (left == 0)
            return vector;

        return new SmtBitVectorValue(((vector.Value << left) | (vector.Value >> (vector.Width - left))) & vector.Mask, vector.Width);
    }

    #endregion
}
