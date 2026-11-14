using System.Numerics;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>The core and arithmetic operators, and arrays' select and store, on closed values.</summary>
internal static class SmtBuiltins
{
    #region Functions

    /// <summary>Applies a core, arithmetic or array operator.</summary>
    /// <param name="op">The operator's name.</param>
    /// <param name="args">The argument values.</param>
    /// <returns>The value, or null when the operator is none of these.</returns>
    /// <exception cref="SmtPartialException">The operator is undefined here (division by zero) or the sorts do not fit.</exception>
    public static SmtValue? Apply(string op, List<SmtValue> args)
    {
        switch (op)
        {
            case "not":
                return SmtBool.Of(!Bool(args[0]));
            case "xor":
                return SmtBool.Of(args.Aggregate(false, (acc, v) => acc ^ Bool(v)));
            case "=":
                for (var i = 0; i + 1 < args.Count; i++)
                {
                    if (!args[i].SameAs(args[i + 1]))
                        return SmtBool.FALSE;
                }

                return SmtBool.TRUE;
            case "distinct":
                for (var i = 0; i < args.Count; i++)
                {
                    for (var j = i + 1; j < args.Count; j++)
                    {
                        if (args[i].SameAs(args[j]))
                            return SmtBool.FALSE;
                    }
                }

                return SmtBool.TRUE;
            case "+":
                return Fold(args, (a, b) => a + b);
            case "*":
                return Fold(args, (a, b) => a * b);
            case "-":
                return args.Count == 1 ? new SmtNumber(-Number(args[0]).Value, Number(args[0]).IsReal) : Fold(args, (a, b) => a - b);
            case "/":
                return Divide(args);
            case "div":
                return IntDivision(args, remainder: false);
            case "mod":
                return IntDivision(args, remainder: true);
            case "abs":
                var number = Number(args[0]);
                return new SmtNumber(number.Value.Sign < 0 ? -number.Value : number.Value, number.IsReal);
            case "<=":
                return Chain(args, c => c <= 0);
            case "<":
                return Chain(args, c => c < 0);
            case ">=":
                return Chain(args, c => c >= 0);
            case ">":
                return Chain(args, c => c > 0);
            case "to_real":
                return new SmtNumber(Number(args[0]).Value, true);
            case "to_int":
                return new SmtNumber(Rational.Of(Number(args[0]).Value.Floor()), false);
            case "is_int":
                return SmtBool.Of(Number(args[0]).Value.IsInteger);
            case "select" when args.Count == 2:
                return Array(args[0]).Select(args[1]);
            case "store" when args.Count == 3:
                return Array(args[0]).Store(args[1], args[2]);
            default:
                return null;
        }
    }

    /// <summary>A value that must be Boolean.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Its truth.</returns>
    public static bool Bool(SmtValue value)
    {
        return value is SmtBool b ? b.Value : throw new SmtPartialException("sort mismatch (Bool)");
    }

    /// <summary>A value that must be a number.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    public static SmtNumber Number(SmtValue value)
    {
        return value as SmtNumber ?? throw new SmtPartialException("sort mismatch (Int/Real)");
    }

    private static SmtArray Array(SmtValue value)
    {
        return value as SmtArray ?? throw new SmtPartialException("sort mismatch (Array)");
    }

    private static SmtNumber Fold(List<SmtValue> args, Func<Rational, Rational, Rational> op)
    {
        var first = Number(args[0]);
        var value = first.Value;
        var isReal = first.IsReal;
        for (var i = 1; i < args.Count; i++)
        {
            var next = Number(args[i]);
            value = op(value, next.Value);
            isReal |= next.IsReal;
        }

        return new SmtNumber(value, isReal);
    }

    private static SmtNumber Divide(List<SmtValue> args)
    {
        var value = Number(args[0]).Value;
        for (var i = 1; i < args.Count; i++)
        {
            var divisor = Number(args[i]).Value;
            if (divisor.Sign == 0)
                throw new SmtPartialException("division by zero (uninterpreted)");
            value /= divisor;
        }

        return new SmtNumber(value, true);
    }

    private static SmtNumber IntDivision(List<SmtValue> args, bool remainder)
    {
        // SMT-LIB Ints: m = n*q + r with 0 <= r < |n|; for n = 0 the result is not fixed.
        var m = Number(args[0]).Value;
        var n = Number(args[1]).Value;
        if (!m.IsInteger || !n.IsInteger)
            throw new SmtPartialException("div/mod of a non-integer");
        if (n.Sign == 0)
            throw new SmtPartialException("division by zero (uninterpreted)");

        var q = BigInteger.DivRem(m.Numerator, n.Numerator, out var r);
        if (r.Sign < 0)
        {
            r += BigInteger.Abs(n.Numerator);
            q = n.Numerator.Sign > 0 ? q - 1 : q + 1;
        }

        return new SmtNumber(Rational.Of(remainder ? r : q), false);
    }

    private static SmtBool Chain(List<SmtValue> args, Func<int, bool> holds)
    {
        for (var i = 0; i + 1 < args.Count; i++)
        {
            if (!holds(Number(args[i]).Value.CompareTo(Number(args[i + 1]).Value)))
                return SmtBool.FALSE;
        }

        return SmtBool.TRUE;
    }

    #endregion
}
