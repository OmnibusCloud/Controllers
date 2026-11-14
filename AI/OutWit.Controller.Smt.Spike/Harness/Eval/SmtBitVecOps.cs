using System.Numerics;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>The SMT-LIB 2.6 bit-vector operators (FixedSizeBitVectors, QF_BV), with Z3's bv2nat/bv2int/int2bv.</summary>
internal static class SmtBitVecOps
{
    #region Functions

    /// <summary>Applies a bit-vector operator.</summary>
    /// <param name="op">The operator's name.</param>
    /// <param name="args">The argument values.</param>
    /// <returns>The value, or null when the operator is no bit-vector operator.</returns>
    public static SmtValue? Apply(string op, List<SmtValue> args)
    {
        switch (op)
        {
            case "concat":
                var result = Bv(args[0]);
                for (var i = 1; i < args.Count; i++)
                {
                    var next = Bv(args[i]);
                    result = new SmtBitVec((result.Value << next.Width) | next.Value, result.Width + next.Width);
                }

                return result;
            case "bvnot":
                return Make(Bv(args[0]).Value ^ Bv(args[0]).Mask, args[0]);
            case "bvneg":
                return Make(-Bv(args[0]).Value, args[0]);
            case "bvand":
                return Fold(args, (a, b) => a & b);
            case "bvor":
                return Fold(args, (a, b) => a | b);
            case "bvxor":
                return Fold(args, (a, b) => a ^ b);
            case "bvnand":
                return Make(~(Bv(args[0]).Value & Bv(args[1]).Value), args[0]);
            case "bvnor":
                return Make(~(Bv(args[0]).Value | Bv(args[1]).Value), args[0]);
            case "bvxnor":
                return Make(~(Bv(args[0]).Value ^ Bv(args[1]).Value), args[0]);
            case "bvadd":
                return Fold(args, (a, b) => a + b);
            case "bvsub":
                return Fold(args, (a, b) => a - b);
            case "bvmul":
                return Fold(args, (a, b) => a * b);
            case "bvudiv":
                return Udiv(Bv(args[0]), Bv(args[1]));
            case "bvurem":
                return Urem(Bv(args[0]), Bv(args[1]));
            case "bvsdiv":
                return Sdiv(Bv(args[0]), Bv(args[1]));
            case "bvsrem":
                return Srem(Bv(args[0]), Bv(args[1]));
            case "bvsmod":
                return Smod(Bv(args[0]), Bv(args[1]));
            case "bvshl":
                return Shift(Bv(args[0]), Bv(args[1]), (x, n) => x << n, _ => 0);
            case "bvlshr":
                return Shift(Bv(args[0]), Bv(args[1]), (x, n) => x >> n, _ => 0);
            case "bvashr":
                var s = Bv(args[0]);
                return Shift(s, Bv(args[1]), (_, n) => s.Signed >> n, x => x.Negative ? x.Mask : 0);
            case "bvult":
                return SmtBool.Of(Bv(args[0]).Value < Bv(args[1]).Value);
            case "bvule":
                return SmtBool.Of(Bv(args[0]).Value <= Bv(args[1]).Value);
            case "bvugt":
                return SmtBool.Of(Bv(args[0]).Value > Bv(args[1]).Value);
            case "bvuge":
                return SmtBool.Of(Bv(args[0]).Value >= Bv(args[1]).Value);
            case "bvslt":
                return SmtBool.Of(Bv(args[0]).Signed < Bv(args[1]).Signed);
            case "bvsle":
                return SmtBool.Of(Bv(args[0]).Signed <= Bv(args[1]).Signed);
            case "bvsgt":
                return SmtBool.Of(Bv(args[0]).Signed > Bv(args[1]).Signed);
            case "bvsge":
                return SmtBool.Of(Bv(args[0]).Signed >= Bv(args[1]).Signed);
            case "bvcomp":
                return new SmtBitVec(Bv(args[0]).SameAs(Bv(args[1])) ? 1 : 0, 1);
            case "bv2nat":
            case "bv2int":
            case "ubv_to_int":
                return new SmtNumber(Rational.Of(Bv(args[0]).Value), false);
            case "sbv_to_int":
                return new SmtNumber(Rational.Of(Bv(args[0]).Signed), false);
            default:
                return null;
        }
    }

    /// <summary>Applies an indexed bit-vector operator: <c>((_ extract i j) x)</c> and the like.</summary>
    /// <param name="op">The operator's name.</param>
    /// <param name="indices">Its numeric indices.</param>
    /// <param name="args">The argument values.</param>
    /// <returns>The value, or null when the operator is no indexed bit-vector operator.</returns>
    public static SmtValue? ApplyIndexed(string op, List<int> indices, List<SmtValue> args)
    {
        switch (op)
        {
            case "extract":
                var x = Bv(args[0]);
                var width = indices[0] - indices[1] + 1;
                return new SmtBitVec((x.Value >> indices[1]) & ((BigInteger.One << width) - 1), width);
            case "zero_extend":
                return new SmtBitVec(Bv(args[0]).Value, Bv(args[0]).Width + indices[0]);
            case "sign_extend":
                return new SmtBitVec(Bv(args[0]).Signed, Bv(args[0]).Width + indices[0]);
            case "repeat":
                var unit = Bv(args[0]);
                var repeated = BigInteger.Zero;
                for (var i = 0; i < indices[0]; i++)
                    repeated = (repeated << unit.Width) | unit.Value;
                return new SmtBitVec(repeated, unit.Width * indices[0]);
            case "rotate_left":
                return Rotate(Bv(args[0]), indices[0]);
            case "rotate_right":
                var r = Bv(args[0]);
                return Rotate(r, r.Width - indices[0] % r.Width);
            case "int2bv":
            case "nat2bv":
            case "int_to_bv":
                return new SmtBitVec(SmtBuiltins.Number(args[0]).Value.Floor(), indices[0]);
            default:
                return null;
        }
    }

    private static SmtBitVec Bv(SmtValue value)
    {
        return value as SmtBitVec ?? throw new SmtPartialException("sort mismatch (BitVec)");
    }

    private static SmtBitVec Make(BigInteger value, SmtValue like)
    {
        return new SmtBitVec(value, Bv(like).Width);
    }

    private static SmtBitVec Fold(List<SmtValue> args, Func<BigInteger, BigInteger, BigInteger> op)
    {
        var first = Bv(args[0]);
        var value = first.Value;
        for (var i = 1; i < args.Count; i++)
        {
            var next = Bv(args[i]);
            if (next.Width != first.Width)
                throw new SmtPartialException("bit-vector widths differ");
            value = op(value, next.Value);
        }

        return new SmtBitVec(value, first.Width);
    }

    private static SmtBitVec Udiv(SmtBitVec s, SmtBitVec t)
    {
        return t.Value.IsZero ? new SmtBitVec(s.Mask, s.Width) : new SmtBitVec(s.Value / t.Value, s.Width);
    }

    private static SmtBitVec Urem(SmtBitVec s, SmtBitVec t)
    {
        return t.Value.IsZero ? s : new SmtBitVec(s.Value % t.Value, s.Width);
    }

    private static SmtBitVec Neg(SmtBitVec x)
    {
        return new SmtBitVec(-x.Value, x.Width);
    }

    private static SmtBitVec Sdiv(SmtBitVec s, SmtBitVec t)
    {
        return (s.Negative, t.Negative) switch
        {
            (false, false) => Udiv(s, t),
            (true, false) => Neg(Udiv(Neg(s), t)),
            (false, true) => Neg(Udiv(s, Neg(t))),
            _ => Udiv(Neg(s), Neg(t))
        };
    }

    private static SmtBitVec Srem(SmtBitVec s, SmtBitVec t)
    {
        return (s.Negative, t.Negative) switch
        {
            (false, false) => Urem(s, t),
            (true, false) => Neg(Urem(Neg(s), t)),
            (false, true) => Urem(s, Neg(t)),
            _ => Neg(Urem(Neg(s), Neg(t)))
        };
    }

    private static SmtBitVec Smod(SmtBitVec s, SmtBitVec t)
    {
        var absS = s.Negative ? Neg(s) : s;
        var absT = t.Negative ? Neg(t) : t;
        var u = Urem(absS, absT);
        if (u.Value.IsZero)
            return u;

        return (s.Negative, t.Negative) switch
        {
            (false, false) => u,
            (true, false) => new SmtBitVec(-u.Value + t.Value, s.Width),
            (false, true) => new SmtBitVec(u.Value + t.Value, s.Width),
            _ => Neg(u)
        };
    }

    private static SmtBitVec Shift(SmtBitVec x, SmtBitVec amount, Func<BigInteger, int, BigInteger> shift, Func<SmtBitVec, BigInteger> overflow)
    {
        return amount.Value >= x.Width ? new SmtBitVec(overflow(x), x.Width) : new SmtBitVec(shift(x.Value, (int)amount.Value), x.Width);
    }

    private static SmtBitVec Rotate(SmtBitVec x, int left)
    {
        left %= x.Width;
        return left == 0 ? x : new SmtBitVec((x.Value << left) | (x.Value >> (x.Width - left)), x.Width);
    }

    #endregion
}
