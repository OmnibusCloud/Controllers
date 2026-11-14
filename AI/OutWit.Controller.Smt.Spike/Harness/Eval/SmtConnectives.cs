using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// and, or and =&gt; in three values: an argument that cannot be closed only makes the whole
/// partial when the known arguments do not decide it (<c>(or true x)</c> is true whatever x is).
/// </summary>
internal static class SmtConnectives
{
    #region Functions

    /// <summary>Evaluates and / or / =&gt; over the items of an application.</summary>
    /// <param name="evaluator">The evaluator.</param>
    /// <param name="op">and, or or =&gt;.</param>
    /// <param name="items">The application's items (the operator first).</param>
    /// <param name="scope">The local names.</param>
    /// <returns>The truth value.</returns>
    /// <exception cref="SmtPartialException">The known arguments do not decide it and one cannot be closed.</exception>
    public static bool Eval(SmtEvaluator evaluator, string op, IReadOnlyList<SExpr> items, SmtScope? scope)
    {
        SmtPartialException? open = null;
        var count = items.Count - 1;
        for (var i = 1; i < items.Count; i++)
        {
            bool value;
            try
            {
                value = evaluator.Truth(items[i], scope);
            }
            catch (SmtPartialException exception)
            {
                open ??= exception;
                continue;
            }

            switch (op)
            {
                case "and" when !value:
                    return false;
                case "or" when value:
                    return true;
                // (=> a b c) is (=> a (=> b c)): a false premise or a true conclusion decides it.
                case "=>" when i < count && !value:
                    return true;
                case "=>" when i == count && value:
                    return true;
            }
        }

        if (open != null)
            throw open;

        // Nothing decided early: and = true, or = false, => = every premise true and the conclusion false.
        return op == "and";
    }

    #endregion
}
