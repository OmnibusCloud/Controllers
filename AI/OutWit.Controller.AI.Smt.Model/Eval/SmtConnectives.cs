using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// <c>and</c>, <c>or</c> and <c>=&gt;</c> in three values: an argument that is not evaluated
/// leaves the connective undecided only when the arguments that are evaluated do not decide it.
/// <para>
/// Why a false conjunct may decide a conjunction whose other conjunct was not evaluated, and
/// the answer may still be called wrong for it: the other conjunct is a formula of the problem,
/// and in every interpretation it is true or false. Whatever a quantified formula, a division
/// by zero or an operator the evaluator does not compute comes to under the model, a
/// conjunction with a false conjunct is false. The same holds for a true disjunct, a false
/// premise and a true conclusion.
/// </para>
/// <para>
/// It holds only for an argument that is merely undecided
/// (<see cref="SmtPartialKind.Undecided"/>). An argument the evaluator cannot make sense of
/// (<see cref="SmtPartialKind.Doubt"/>) leaves the whole assertion undecided, because then the
/// problem may not mean what was read; and a budget (<see cref="SmtPartialKind.Terminal"/>)
/// ends the evaluation. Whether the deciding argument itself rests on a symbol the model left
/// out is not asked here: the validator asks it of the whole assertion.
/// </para>
/// </summary>
internal sealed class SmtConnectives
{
    #region Fields

    private readonly SmtMeter m_meter;
    private readonly SmtEvaluator m_evaluator;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates the connectives of one validation.
    /// </summary>
    /// <param name="run">The validation.</param>
    /// <param name="evaluator">The walker of terms.</param>
    public SmtConnectives(SmtRun run, SmtEvaluator evaluator)
    {
        m_meter = run.Meter;
        m_evaluator = evaluator;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Evaluates <c>and</c>, <c>or</c> or <c>=&gt;</c> over the items of an application, up to
    /// its last argument. Where nothing before the last argument decided the connective and
    /// nothing before it was left undecided, the connective is exactly as true as its last
    /// argument: that argument is handed back for the caller to go on with, so a chain of
    /// connectives, each the last argument of the one before, costs no nesting.
    /// </summary>
    /// <param name="connective">The connective's name.</param>
    /// <param name="items">The application's items, the connective first.</param>
    /// <param name="scope">The local names the application stands among, or null.</param>
    /// <param name="depth">The level of nesting the application is at.</param>
    /// <param name="truth">The truth of the application, when it is decided here.</param>
    /// <returns>Null when the application is decided here; otherwise its last argument, whose truth is the application's.</returns>
    /// <exception cref="SmtPartialException">An argument is not evaluated and the others do not decide.</exception>
    public SmtExpression? Decide(string connective, IReadOnlyList<SmtExpression> items, SmtScope? scope, int depth, out bool truth)
    {
        truth = false;
        if (items.Count < 2 || (items.Count < 3 && connective == SmtVocabulary.IMPLIES))
            throw m_meter.Doubt();

        // (=> a b c) is (=> a (=> b c)): false exactly when every premise is true and the
        // conclusion, the last argument, is false.
        var last = items.Count - 1;
        SmtPartialException? open = null;
        for (var index = 1; index <= last; index++)
        {
            // Every conjunct so far true, every disjunct false, every premise true: what is
            // left of the connective is its last argument.
            if (index == last && open == null)
                return items[last];

            var mark = scope?.Mark ?? 0;
            bool value;
            try
            {
                value = m_evaluator.Truth(items[index], scope, depth + 1);
            }
            catch (SmtPartialException exception) when (exception.Kind == SmtPartialKind.Undecided)
            {
                // The argument may have left a let half-way: its names go before the next one is read.
                scope?.Unwind(mark);
                m_meter.ChargeCaught(exception, depth);
                open ??= exception;
                continue;
            }

            switch (connective)
            {
                case SmtVocabulary.AND when !value:
                    return null;
                case SmtVocabulary.OR when value:
                case SmtVocabulary.IMPLIES when index < last && !value:
                case SmtVocabulary.IMPLIES when index == last && value:
                    truth = true;
                    return null;
            }
        }

        // The last argument was read here, since one before it was left undecided, and it did
        // not decide either: so is the connective undecided, for the first reason met; from
        // here, not from where it was met.
        throw new SmtPartialException(SmtPartialKind.Undecided, open?.Reason ?? SmtValidationReasons.ILL_FORMED_TERM, open?.Detail, depth);
    }

    #endregion
}
