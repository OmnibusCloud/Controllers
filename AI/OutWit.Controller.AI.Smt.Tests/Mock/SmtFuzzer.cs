using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Damages s-expressions at random, from a seed: one place of a list of forms is picked and
/// changed the way a careless or a hostile hand changes a problem or a model. An argument is
/// dropped, doubled or swapped with its neighbour; an operator becomes another; a value becomes
/// a value of another sort; a term is wrapped, emptied or replaced by one from elsewhere.
/// The same seed makes the same damage, on every machine.
/// </summary>
internal sealed class SmtFuzzer
{
    #region Constants

    private static readonly string[] SYMBOLS =
    [
        "and", "or", "not", "=>", "xor", "=", "distinct", "ite", "+", "-", "*", "/", "div", "mod", "abs", "<", "<=", ">", ">=",
        "to_real", "to_int", "is_int", "select", "store", "concat", "bvnot", "bvneg", "bvand", "bvor", "bvadd", "bvmul", "bvudiv",
        "bvurem", "bvsdiv", "bvsrem", "bvsmod", "bvshl", "bvlshr", "bvashr", "bvult", "bvslt", "bv2nat", "str.++", "str.len",
        "str.at", "str.substr", "str.indexof", "str.replace", "str.to_int", "str.in_re", "true", "false", "let", "forall", "exists",
        "match", "_", "!", "as", "par", "lambda", "extract", "zero_extend", "repeat", "int2bv", "is", "const", "as-array",
        "declare-const", "declare-fun", "define-fun", "define-fun-rec", "declare-datatypes", "declare-sort", "assert",
        "Int", "Real", "Bool", "String", "Array", "BitVec", "x", "y", "f", "nil", "cons", "hd", "U!val!0", "k!0", "x!0", "undeclared"
    ];

    private static readonly SmtExpression[] VALUES =
    [
        SmtExpression.Symbol("true"), SmtExpression.Symbol("false"), SmtExpression.Atom(SmtExpressionKind.Numeral, "0"),
        SmtExpression.Atom(SmtExpressionKind.Numeral, "1"), SmtExpression.Atom(SmtExpressionKind.Numeral, "340282366920938463463374607431768211456"),
        SmtExpression.Atom(SmtExpressionKind.Decimal, "1.5"), SmtExpression.Atom(SmtExpressionKind.Hexadecimal, "#x0"),
        SmtExpression.Atom(SmtExpressionKind.Hexadecimal, "#xff"), SmtExpression.Atom(SmtExpressionKind.Binary, "#b1"),
        SmtExpression.Atom(SmtExpressionKind.String, "s"), SmtExpression.Atom(SmtExpressionKind.String, "\\u{}"),
        SmtExpression.Atom(SmtExpressionKind.Keyword, ":named"), SmtExpression.List(),
        SmtExpression.List(SmtExpression.Symbol("-"), SmtExpression.Atom(SmtExpressionKind.Numeral, "1")),
        SmtExpression.List(SmtExpression.Symbol("_"), SmtExpression.Symbol("bv3"), SmtExpression.Atom(SmtExpressionKind.Numeral, "8")),
        SmtExpression.List(SmtExpression.Symbol("div"), SmtExpression.Atom(SmtExpressionKind.Numeral, "1"), SmtExpression.Atom(SmtExpressionKind.Numeral, "0")),
        // Tokens no reader takes, written as they are: made without the check a program's atom gets.
        SmtExpression.Token(SmtExpressionKind.Numeral, "007", 0), SmtExpression.Token(SmtExpressionKind.Hexadecimal, "#xzz", 0),
        SmtExpression.Token(SmtExpressionKind.Decimal, "1.2.3", 0), SmtExpression.QuotedSymbol("let"), SmtExpression.QuotedSymbol("")
    ];

    private const int DAMAGES = 9;

    #endregion

    #region Fields

    private readonly Random m_random;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a fuzzer.
    /// </summary>
    /// <param name="seed">What every choice follows from.</param>
    public SmtFuzzer(int seed)
    {
        m_random = new Random(seed);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Damages one place of a list of forms.
    /// </summary>
    /// <param name="forms">The forms: commands of a problem, or the items of a model.</param>
    /// <returns>The forms with one place changed; a form may be gone.</returns>
    public IReadOnlyList<SmtExpression> Damage(IReadOnlyList<SmtExpression> forms)
    {
        if (forms.Count == 0)
            return forms;

        var whole = SmtExpression.List(forms);
        var target = m_random.Next(1, Count(whole));
        var position = 0;
        return Rebuild(whole, target, ref position, whole).Items;
    }

    /// <summary>
    /// Damages one place inside the last part of one form, and leaves the form's own shape:
    /// the term of an assertion, the body of a definition, the value of a pair. The form stays
    /// a command, or an item of a model, that can be read; what it says is what changed.
    /// </summary>
    /// <param name="forms">The forms: commands of a problem, or the items of a model.</param>
    /// <returns>The forms with one of them changed inside.</returns>
    public IReadOnlyList<SmtExpression> DamageInside(IReadOnlyList<SmtExpression> forms)
    {
        var candidates = Enumerable.Range(0, forms.Count).Where(index => forms[index].Items.Count >= 2).ToList();
        if (candidates.Count == 0)
            return Damage(forms);

        var chosen = candidates[m_random.Next(candidates.Count)];
        var items = forms[chosen].Items.ToArray();
        items[^1] = Damage([items[^1]])[0];

        var damaged = forms.ToArray();
        damaged[chosen] = SmtExpression.List(items);
        return damaged;
    }

    /// <summary>
    /// A number below a bound, from the fuzzer's own sequence.
    /// </summary>
    /// <param name="bound">The bound, above zero.</param>
    /// <returns>A number from zero up to the bound.</returns>
    public int Next(int bound)
    {
        return m_random.Next(bound);
    }

    private static int Count(SmtExpression expression)
    {
        var count = 0;
        var pending = new Stack<SmtExpression>();
        pending.Push(expression);
        while (pending.Count > 0)
        {
            count++;
            foreach (var item in pending.Pop().Items)
                pending.Push(item);
        }

        return count;
    }

    /// <summary>
    /// Copies an s-expression, changing the node whose number in the walk is the target.
    /// </summary>
    private SmtExpression Rebuild(SmtExpression expression, int target, ref int position, SmtExpression whole)
    {
        var own = position++;
        if (own == target)
        {
            // What stands under the damaged node is not walked: its numbers are skipped.
            position += Count(expression) - 1;
            return Damaged(expression, whole);
        }

        if (!expression.IsList || target < own || own + Count(expression) <= target)
        {
            position = own + Count(expression);
            return expression;
        }

        var items = new SmtExpression[expression.Items.Count];
        for (var index = 0; index < items.Length; index++)
            items[index] = Rebuild(expression.Items[index], target, ref position, whole);

        return SmtExpression.List(items);
    }

    private SmtExpression Damaged(SmtExpression expression, SmtExpression whole)
    {
        var items = expression.Items.ToList();
        switch (m_random.Next(DAMAGES))
        {
            case 0 when items.Count > 0:
                items.RemoveAt(m_random.Next(items.Count));
                return SmtExpression.List(items);
            case 1 when items.Count > 0:
                items.Insert(m_random.Next(items.Count), items[m_random.Next(items.Count)]);
                return SmtExpression.List(items);
            case 2 when items.Count > 1:
                var first = m_random.Next(items.Count - 1);
                (items[first], items[first + 1]) = (items[first + 1], items[first]);
                return SmtExpression.List(items);
            case 3 when items.Count > 0:
                items[0] = SmtExpression.Symbol(SYMBOLS[m_random.Next(SYMBOLS.Length)]);
                return SmtExpression.List(items);
            case 4:
                return SmtExpression.Symbol(SYMBOLS[m_random.Next(SYMBOLS.Length)]);
            case 5:
                return SmtExpression.List(expression);
            case 6:
                return Any(whole);
            case 7 when expression.Kind == SmtExpressionKind.Symbol:
                return SmtExpression.QuotedSymbol(expression.Text);
            default:
                return VALUES[m_random.Next(VALUES.Length)];
        }
    }

    /// <summary>
    /// A term from anywhere in the forms: a value of some sort where another was meant.
    /// </summary>
    private SmtExpression Any(SmtExpression whole)
    {
        var target = m_random.Next(1, Count(whole));
        var pending = new Stack<SmtExpression>();
        pending.Push(whole);
        for (var position = 0; pending.Count > 0; position++)
        {
            var expression = pending.Pop();
            if (position == target)
                return expression;

            foreach (var item in expression.Items)
                pending.Push(item);
        }

        return whole;
    }

    #endregion
}
