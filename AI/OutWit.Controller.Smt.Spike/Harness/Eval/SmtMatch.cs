using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>SMT-LIB 2.6 <c>match</c> over a datatype value: constructor patterns, nullary constructors, a variable.</summary>
internal static class SmtMatch
{
    #region Functions

    /// <summary>Evaluates a match term.</summary>
    /// <param name="evaluator">The evaluator.</param>
    /// <param name="declarations">The problem's declarations (to tell a nullary constructor from a variable).</param>
    /// <param name="term">The match term.</param>
    /// <param name="scope">The local names.</param>
    /// <returns>The value of the first case that matches.</returns>
    /// <exception cref="SmtPartialException">No case matches, or the value is no datatype value.</exception>
    public static SmtValue Eval(SmtEvaluator evaluator, SmtDeclarations declarations, SExpr term, SmtScope? scope)
    {
        if (evaluator.Eval(term.Items[1], scope) is not SmtDatatypeValue value)
            throw new SmtPartialException("match over no datatype value");

        foreach (var @case in term.Items[2].Items)
        {
            var pattern = @case.Items[0];
            var body = @case.Items[1];
            if (!pattern.IsList)
            {
                if (declarations.Constructors.ContainsKey(pattern.Text))
                {
                    if (value.Constructor == pattern.Text)
                        return evaluator.Eval(body, scope);
                    continue;
                }

                return evaluator.Eval(body, new SmtScope(new Dictionary<string, SmtValue> { [pattern.Text] = value }, scope));
            }

            if (pattern.Items[0].Text != value.Constructor)
                continue;

            var names = new Dictionary<string, SmtValue>();
            for (var i = 1; i < pattern.Items.Count && i - 1 < value.Fields.Count; i++)
                names[pattern.Items[i].Text] = value.Fields[i - 1];
            return evaluator.Eval(body, new SmtScope(names, scope));
        }

        throw new SmtPartialException("no match case");
    }

    #endregion
}
