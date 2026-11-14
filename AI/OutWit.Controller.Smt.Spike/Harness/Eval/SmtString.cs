namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>A string of the SMT-LIB theory of strings, as Unicode code points.</summary>
internal sealed class SmtString : SmtValue
{
    #region Constructors

    /// <summary>Makes a string.</summary>
    /// <param name="codePoints">Its characters as code points.</param>
    public SmtString(IReadOnlyList<int> codePoints)
    {
        CodePoints = codePoints;
    }

    #endregion

    #region Functions

    /// <summary>Reads an SMT-LIB 2.6 string literal's content: <c>\u{...}</c> and <c>\ud</c>... escapes.</summary>
    /// <param name="literal">The literal's content with <c>""</c> already unescaped.</param>
    /// <returns>The string.</returns>
    public static SmtString FromLiteral(string literal)
    {
        var points = new List<int>();
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] == '\\' && i + 1 < literal.Length && literal[i + 1] == 'u')
            {
                if (i + 2 < literal.Length && literal[i + 2] == '{')
                {
                    var close = literal.IndexOf('}', i + 3);
                    if (close > i + 3 && close - i - 3 <= 5 && IsHex(literal, i + 3, close))
                    {
                        points.Add(Convert.ToInt32(literal[(i + 3)..close], 16));
                        i = close;
                        continue;
                    }
                }
                else if (i + 5 < literal.Length && IsHex(literal, i + 2, i + 6))
                {
                    points.Add(Convert.ToInt32(literal.Substring(i + 2, 4), 16));
                    i += 5;
                    continue;
                }
            }

            if (char.IsHighSurrogate(literal[i]) && i + 1 < literal.Length && char.IsLowSurrogate(literal[i + 1]))
            {
                points.Add(char.ConvertToUtf32(literal[i], literal[i + 1]));
                i++;
                continue;
            }

            points.Add(literal[i]);
        }

        return new SmtString(points);
    }

    private static bool IsHex(string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (!Uri.IsHexDigit(text[i]))
                return false;
        }

        return to > from;
    }

    /// <inheritdoc />
    public override bool SameAs(SmtValue other)
    {
        return other is SmtString s && s.CodePoints.SequenceEqual(CodePoints);
    }

    /// <inheritdoc />
    public override string Key()
    {
        return "s:" + string.Join(",", CodePoints);
    }

    #endregion

    #region Properties

    /// <summary>The characters as code points.</summary>
    public IReadOnlyList<int> CodePoints { get; }

    #endregion
}
