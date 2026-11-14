using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// Turns the function Z3 prints behind an array (an <c>as-array</c> function or a lambda) into a
/// finite map when its body has the usual shape, a chain
/// <c>(ite (= x c1) v1 (ite (= x c2) v2 ... else))</c> whose <c>else</c> does not mention x.
/// Only a finite map can be compared with another array.
/// </summary>
internal static class SmtArrayModels
{
    #region Functions

    /// <summary>The finite map of a one-parameter function body, or null when the body has another shape.</summary>
    /// <param name="evaluator">The evaluator.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="body">The body.</param>
    /// <param name="scope">The scope the body is closed in.</param>
    /// <returns>The array, or null.</returns>
    public static SmtArray? FromFunction(SmtEvaluator evaluator, string parameter, SExpr body, SmtScope? scope)
    {
        var entries = new List<(SExpr Index, SExpr Value)>();
        var current = body;
        while (current.IsApp("ite") && current.Items.Count == 4)
        {
            var condition = current.Items[1];
            if (!condition.IsApp("=") || condition.Items.Count != 3)
                return null;

            SExpr index;
            if (condition.Items[1].IsSymbol(parameter) && !Mentions(condition.Items[2], parameter))
                index = condition.Items[2];
            else if (condition.Items[2].IsSymbol(parameter) && !Mentions(condition.Items[1], parameter))
                index = condition.Items[1];
            else
                return null;

            if (Mentions(current.Items[2], parameter))
                return null;

            entries.Add((index, current.Items[2]));
            current = current.Items[3];
        }

        if (Mentions(current, parameter))
            return null;

        var array = SmtArray.Constant(evaluator.Eval(current, scope));
        // The first condition wins: store the entries from the last to the first.
        for (var i = entries.Count - 1; i >= 0; i--)
            array = array.Store(evaluator.Eval(entries[i].Index, scope), evaluator.Eval(entries[i].Value, scope));
        return array;
    }

    private static bool Mentions(SExpr term, string name)
    {
        if (!term.IsList)
            return term.IsSymbol(name);

        foreach (var item in term.Items)
        {
            if (Mentions(item, name))
                return true;
        }

        return false;
    }

    #endregion
}
