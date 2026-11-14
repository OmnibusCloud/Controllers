using System.Text;

namespace OutWit.Controller.Smt.Spike.Lang;

/// <summary>
/// A minimal SMT-LIB 2.6 reader for the spike: comments, string literals with <c>""</c> escapes,
/// <c>|quoted symbols|</c>, keywords, numerals, decimals, <c>#x</c>/<c>#b</c> literals, and
/// nesting without recursion (deep terms cannot overflow the reader). The production lexer and
/// sanitizer with the shape limits of the specification are SM-1's; this one only refuses what
/// it cannot read.
/// </summary>
internal sealed class SmtReader
{
    #region Fields

    private readonly string m_text;
    private int m_position;
    private int m_line = 1;

    #endregion

    #region Constructors

    private SmtReader(string text)
    {
        m_text = text;
    }

    #endregion

    #region Functions

    /// <summary>Reads every top-level s-expression of a text.</summary>
    /// <param name="text">SMT-LIB text (a script, a model, a get-value answer).</param>
    /// <returns>The top-level s-expressions in order.</returns>
    /// <exception cref="FormatException">The text is not well-formed s-expressions.</exception>
    public static List<SExpr> ReadAll(string text)
    {
        return new SmtReader(text).ReadAllForms();
    }

    private List<SExpr> ReadAllForms()
    {
        var top = new List<SExpr>();
        var stack = new Stack<(List<SExpr> Items, int Line)>();

        while (true)
        {
            SkipSpaceAndComments();
            if (m_position >= m_text.Length)
                break;

            var c = m_text[m_position];
            if (c == '(')
            {
                stack.Push((new List<SExpr>(), m_line));
                m_position++;
                continue;
            }

            if (c == ')')
            {
                if (stack.Count == 0)
                    throw new FormatException($"line {m_line}: ')' without '('");

                m_position++;
                var (items, line) = stack.Pop();
                Add(SExpr.List(items, line), stack, top);
                continue;
            }

            Add(ReadAtom(), stack, top);
        }

        if (stack.Count > 0)
            throw new FormatException($"line {stack.Peek().Line}: '(' not closed");

        return top;
    }

    private static void Add(SExpr expr, Stack<(List<SExpr> Items, int Line)> stack, List<SExpr> top)
    {
        if (stack.Count == 0)
            top.Add(expr);
        else
            stack.Peek().Items.Add(expr);
    }

    private void SkipSpaceAndComments()
    {
        while (m_position < m_text.Length)
        {
            var c = m_text[m_position];
            if (c == ';')
            {
                while (m_position < m_text.Length && m_text[m_position] != '\n')
                    m_position++;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (c == '\n')
                    m_line++;
                m_position++;
            }
            else
            {
                return;
            }
        }
    }

    private SExpr ReadAtom()
    {
        var line = m_line;
        var c = m_text[m_position];

        if (c == '"')
            return SExpr.Atom(SExprKind.String, ReadString(), line);

        if (c == '|')
        {
            var end = m_text.IndexOf('|', m_position + 1);
            if (end < 0)
                throw new FormatException($"line {line}: '|' not closed");

            var name = m_text.Substring(m_position + 1, end - m_position - 1);
            m_line += name.Count(ch => ch == '\n');
            m_position = end + 1;
            return SExpr.QuotedSymbol(name, line);
        }

        var start = m_position;
        while (m_position < m_text.Length && !IsDelimiter(m_text[m_position]))
            m_position++;

        var token = m_text.Substring(start, m_position - start);
        return SExpr.Atom(Classify(token), token, line);
    }

    private string ReadString()
    {
        var builder = new StringBuilder();
        m_position++;
        while (true)
        {
            if (m_position >= m_text.Length)
                throw new FormatException($"line {m_line}: string literal not closed");

            var c = m_text[m_position++];
            if (c == '"')
            {
                if (m_position < m_text.Length && m_text[m_position] == '"')
                {
                    builder.Append('"');
                    m_position++;
                    continue;
                }

                return builder.ToString();
            }

            if (c == '\n')
                m_line++;
            builder.Append(c);
        }
    }

    private static bool IsDelimiter(char c)
    {
        return char.IsWhiteSpace(c) || c == '(' || c == ')' || c == '"' || c == ';' || c == '|';
    }

    private static SExprKind Classify(string token)
    {
        if (token.StartsWith(':'))
            return SExprKind.Keyword;
        if (token.StartsWith("#x", StringComparison.Ordinal))
            return SExprKind.Hexadecimal;
        if (token.StartsWith("#b", StringComparison.Ordinal))
            return SExprKind.Binary;
        if (token.Length > 0 && char.IsDigit(token[0]))
            return token.Contains('.') ? SExprKind.Decimal : SExprKind.Numeral;
        return SExprKind.Symbol;
    }

    #endregion
}
