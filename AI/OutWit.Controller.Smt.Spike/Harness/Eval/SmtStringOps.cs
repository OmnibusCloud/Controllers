using System.Numerics;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// The basic operators of the SMT-LIB theory of strings (no regular expressions: a membership
/// test is partial). Indices and lengths count code points, as the theory does.
/// </summary>
internal static class SmtStringOps
{
    #region Functions

    /// <summary>Applies a string operator.</summary>
    /// <param name="op">The operator's name.</param>
    /// <param name="args">The argument values.</param>
    /// <returns>The value, or null when the operator is no string operator handled here.</returns>
    public static SmtValue? Apply(string op, List<SmtValue> args)
    {
        switch (op)
        {
            case "str.++":
                return new SmtString(args.SelectMany(a => Str(a).CodePoints).ToList());
            case "str.len":
                return Int(Str(args[0]).CodePoints.Count);
            case "str.at":
                return Substring(Str(args[0]), Index(args[1]), 1);
            case "str.substr":
                return Substring(Str(args[0]), Index(args[1]), Index(args[2]));
            case "str.prefixof":
                return SmtBool.Of(StartsWith(Str(args[1]).CodePoints, Str(args[0]).CodePoints, 0));
            case "str.suffixof":
                var whole = Str(args[1]).CodePoints;
                var suffix = Str(args[0]).CodePoints;
                return SmtBool.Of(suffix.Count <= whole.Count && StartsWith(whole, suffix, whole.Count - suffix.Count));
            case "str.contains":
                return SmtBool.Of(IndexOf(Str(args[0]).CodePoints, Str(args[1]).CodePoints, 0) >= 0);
            case "str.indexof":
                var start = Index(args[2]);
                var text = Str(args[0]).CodePoints;
                return Int(start < 0 || start > text.Count ? -1 : IndexOf(text, Str(args[1]).CodePoints, (int)start));
            case "str.replace":
                return Replace(Str(args[0]), Str(args[1]), Str(args[2]));
            case "str.<":
                return SmtBool.Of(Compare(Str(args[0]), Str(args[1])) < 0);
            case "str.<=":
                return SmtBool.Of(Compare(Str(args[0]), Str(args[1])) <= 0);
            case "str.to_int":
            case "str.to.int":
                return ToInt(Str(args[0]));
            case "str.from_int":
            case "int.to.str":
                var n = SmtBuiltins.Number(args[0]).Value;
                return n.Sign < 0 ? new SmtString([]) : new SmtString(n.Numerator.ToString().Select(c => (int)c).ToList());
            case "str.is_digit":
                var d = Str(args[0]).CodePoints;
                return SmtBool.Of(d.Count == 1 && d[0] >= '0' && d[0] <= '9');
            case "str.to_code":
                var c1 = Str(args[0]).CodePoints;
                return Int(c1.Count == 1 ? c1[0] : -1);
            case "str.from_code":
                var code = SmtBuiltins.Number(args[0]).Value;
                return code.IsInteger && code.Numerator >= 0 && code.Numerator <= 0x2FFFF ? new SmtString([(int)code.Numerator]) : new SmtString([]);
            case "str.in_re":
            case "str.in.re":
                throw new SmtPartialException("regular expression");
            default:
                return null;
        }
    }

    private static SmtString Str(SmtValue value)
    {
        return value as SmtString ?? throw new SmtPartialException("sort mismatch (String)");
    }

    private static SmtNumber Int(long value)
    {
        return new SmtNumber(Rational.Of(value), false);
    }

    private static long Index(SmtValue value)
    {
        var n = SmtBuiltins.Number(value).Value;
        if (!n.IsInteger)
            throw new SmtPartialException("string index not an integer");
        return n.Numerator > int.MaxValue ? int.MaxValue : n.Numerator < int.MinValue ? int.MinValue : (long)n.Numerator;
    }

    private static SmtString Substring(SmtString s, long offset, long length)
    {
        var points = s.CodePoints;
        if (offset < 0 || length <= 0 || offset >= points.Count)
            return new SmtString([]);
        var count = (int)Math.Min(length, points.Count - offset);
        return new SmtString(points.Skip((int)offset).Take(count).ToList());
    }

    private static bool StartsWith(IReadOnlyList<int> text, IReadOnlyList<int> prefix, int at)
    {
        if (at < 0 || at + prefix.Count > text.Count)
            return false;
        for (var i = 0; i < prefix.Count; i++)
        {
            if (text[at + i] != prefix[i])
                return false;
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<int> text, IReadOnlyList<int> part, int from)
    {
        for (var i = from; i + part.Count <= text.Count; i++)
        {
            if (StartsWith(text, part, i))
                return i;
        }

        return -1;
    }

    private static SmtString Replace(SmtString s, SmtString find, SmtString with)
    {
        // The first occurrence; an empty pattern is found at 0.
        var at = IndexOf(s.CodePoints, find.CodePoints, 0);
        if (at < 0)
            return s;
        var points = s.CodePoints.Take(at).Concat(with.CodePoints).Concat(s.CodePoints.Skip(at + find.CodePoints.Count)).ToList();
        return new SmtString(points);
    }

    private static int Compare(SmtString a, SmtString b)
    {
        for (var i = 0; i < Math.Min(a.CodePoints.Count, b.CodePoints.Count); i++)
        {
            if (a.CodePoints[i] != b.CodePoints[i])
                return a.CodePoints[i].CompareTo(b.CodePoints[i]);
        }

        return a.CodePoints.Count.CompareTo(b.CodePoints.Count);
    }

    private static SmtNumber ToInt(SmtString s)
    {
        if (s.CodePoints.Count == 0 || s.CodePoints.Any(c => c < '0' || c > '9'))
            return Int(-1);
        return new SmtNumber(Rational.Of(BigInteger.Parse(new string(s.CodePoints.Select(c => (char)c).ToArray()))), false);
    }

    #endregion
}
