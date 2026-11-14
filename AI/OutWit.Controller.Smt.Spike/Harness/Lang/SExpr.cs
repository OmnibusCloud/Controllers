using System.Text;

namespace OutWit.Controller.Smt.Spike.Lang;

/// <summary>The kinds of SMT-LIB 2.6 s-expressions the spike's reader tells apart.</summary>
internal enum SExprKind
{
    List,
    Symbol,
    Keyword,
    Numeral,
    Decimal,
    Hexadecimal,
    Binary,
    String
}

/// <summary>
/// One SMT-LIB s-expression: a list of s-expressions or an atom with its text. A quoted symbol
/// carries its name without the bars (<c>|a b|</c> is the symbol <c>a b</c>); a string carries its
/// content with <c>""</c> already unescaped.
/// </summary>
internal sealed class SExpr
{
    #region Constants

    private static readonly IReadOnlyList<SExpr> NO_ITEMS = [];

    // SMT-LIB 2.6 reserved words: a quoted symbol with one of these names keeps its bars.
    private static readonly HashSet<string> RESERVED =
    [
        "!", "_", "as", "BINARY", "DECIMAL", "exists", "forall", "HEXADECIMAL", "let", "match", "NUMERAL", "par", "STRING"
    ];

    #endregion

    #region Constructors

    private SExpr(SExprKind kind, string text, IReadOnlyList<SExpr> items, int line, bool quoted = false)
    {
        Kind = kind;
        Text = text;
        Items = items;
        Line = line;
        Quoted = quoted;
    }

    #endregion

    #region Functions

    /// <summary>Makes an atom.</summary>
    /// <param name="kind">Any kind but <see cref="SExprKind.List"/>.</param>
    /// <param name="text">The atom's text.</param>
    /// <param name="line">The line it started on (1-based), 0 when made by the program.</param>
    /// <returns>The atom.</returns>
    public static SExpr Atom(SExprKind kind, string text, int line = 0)
    {
        return new SExpr(kind, text, NO_ITEMS, line);
    }

    /// <summary>Makes a symbol that was written between bars.</summary>
    /// <param name="name">The name without the bars.</param>
    /// <param name="line">The line it started on.</param>
    /// <returns>The symbol.</returns>
    public static SExpr QuotedSymbol(string name, int line)
    {
        return new SExpr(SExprKind.Symbol, name, NO_ITEMS, line, quoted: true);
    }

    /// <summary>Makes a symbol.</summary>
    /// <param name="name">The symbol's name.</param>
    /// <returns>The symbol.</returns>
    public static SExpr Sym(string name)
    {
        return new SExpr(SExprKind.Symbol, name, NO_ITEMS, 0);
    }

    /// <summary>Makes a list.</summary>
    /// <param name="items">The items.</param>
    /// <param name="line">The line it started on.</param>
    /// <returns>The list.</returns>
    public static SExpr List(IReadOnlyList<SExpr> items, int line = 0)
    {
        return new SExpr(SExprKind.List, "", items, line);
    }

    /// <summary>Whether this is the symbol <paramref name="name"/>.</summary>
    /// <param name="name">A symbol name.</param>
    /// <returns>True for that symbol.</returns>
    public bool IsSymbol(string name)
    {
        return Kind == SExprKind.Symbol && Text == name;
    }

    /// <summary>Whether this is a list whose first item is the symbol <paramref name="head"/>.</summary>
    /// <param name="head">A symbol name.</param>
    /// <returns>True for such a list.</returns>
    public bool IsApp(string head)
    {
        return Kind == SExprKind.List && Items.Count > 0 && Items[0].IsSymbol(head);
    }

    /// <summary>Writes the s-expression in SMT-LIB syntax, atoms separated by single spaces.</summary>
    /// <param name="builder">Where to write.</param>
    public void WriteTo(StringBuilder builder)
    {
        // Without recursion: a term nested a hundred thousand deep is still written.
        var stack = new Stack<(SExpr Expr, int Next)>();
        stack.Push((this, -1));
        while (stack.Count > 0)
        {
            var (expr, next) = stack.Pop();
            if (expr.Kind != SExprKind.List)
            {
                expr.WriteAtom(builder);
                continue;
            }

            if (next < 0)
            {
                builder.Append('(');
                next = 0;
            }

            if (next >= expr.Items.Count)
            {
                builder.Append(')');
                continue;
            }

            if (next > 0)
                builder.Append(' ');
            stack.Push((expr, next + 1));
            stack.Push((expr.Items[next], -1));
        }
    }

    private void WriteAtom(StringBuilder builder)
    {
        switch (Kind)
        {
            case SExprKind.Symbol:
                builder.Append(Quoted && NeedsBars(Text) ? $"|{Text}|" : Text);
                break;
            case SExprKind.String:
                builder.Append('"').Append(Text.Replace("\"", "\"\"")).Append('"');
                break;
            default:
                builder.Append(Text);
                break;
        }
    }

    /// <summary>The s-expression as SMT-LIB text.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        WriteTo(builder);
        return builder.ToString();
    }

    private static bool NeedsBars(string name)
    {
        if (name.Length == 0 || char.IsDigit(name[0]) || RESERVED.Contains(name))
            return true;

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || "~!@$%^&*_-+=<>.?/".Contains(c)) || c > 127)
                return true;
        }

        return false;
    }

    #endregion

    #region Properties

    /// <summary>What the s-expression is.</summary>
    public SExprKind Kind { get; }

    /// <summary>The atom's text; empty for a list.</summary>
    public string Text { get; }

    /// <summary>The list's items; empty for an atom.</summary>
    public IReadOnlyList<SExpr> Items { get; }

    /// <summary>The line the s-expression started on (1-based), 0 when made by the program.</summary>
    public int Line { get; }

    /// <summary>Whether the symbol was written between bars (it keeps them where it needs them).</summary>
    public bool Quoted { get; }

    /// <summary>Whether this is a list.</summary>
    public bool IsList => Kind == SExprKind.List;

    /// <summary>The first item's symbol name, or null when this is no list headed by a symbol.</summary>
    public string? Head => Kind == SExprKind.List && Items.Count > 0 && Items[0].Kind == SExprKind.Symbol ? Items[0].Text : null;

    #endregion
}
