using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// The answer of <c>get-value</c> held to the model, in one validation. The answer is a
/// stranger's text like the model: each of its pairs claims that a term has a value, and no
/// pair is believed before it is checked.
/// <list type="bullet">
/// <item>The answer is taken only when it is the answer of what was asked: one list of pairs,
/// and, where the caller says which terms were asked, as many pairs as terms, each pair's term
/// written as the term asked at its place. A solver echoes a term as it was written to it (the
/// pinned one does so character for character, measured on generated terms of every theory),
/// so an honest answer always reads so. An answer that is not taken is not used at all, and the
/// validation is partial at best (<see cref="SmtValidationReasons.UNREADABLE_VALUES"/>).</item>
/// <item>Each term is evaluated under the model as an assertion is, each value is read among the
/// model's own names as a definition of the model is, and the two are compared as values
/// (<see cref="SmtEquality"/>), not as texts: a solver and the evaluator may spell one value
/// two ways.</item>
/// <item>Another value, with nothing guessed, fails the validation
/// (<see cref="SmtValidationReasons.VALUE_MISMATCH"/>). A term or a value the evaluator does not
/// compute, a comparison it does not decide, and a term that rests on a symbol the model leaves
/// out leave the validation partial, each with its reason: a value is never validated unread,
/// and an answer is never failed for what the evaluator cannot decide.</item>
/// <item>A value is written with nothing of the problem. One that reads a symbol the problem
/// declares or defines, itself or through a name the model wrote with one ("the value of
/// <c>x</c> is <c>x</c>"), may be true and says nothing: it is partial
/// (<see cref="SmtValidationReasons.UNREADABLE_VALUES"/>). The reads are counted while the value
/// is evaluated and compared (<see cref="SmtMeter.References"/>). A value may name what the
/// model itself gives a value: an element of a sort, the function behind an array.</item>
/// <item>The first value of a constant the problem declares and the model leaves out is that
/// constant's value (<see cref="SmtModel.Completions"/>): there is nothing to compare it with.
/// The constant is still evaluated, so that a value of another sort shows, and the value is
/// still read by itself, so that one written with a symbol of the problem shows.</item>
/// </list>
/// The checks spend from the budget of the validation they belong to.
/// </summary>
internal sealed class SmtReportedValues
{
    #region Fields

    private readonly SmtModel m_model;
    private readonly IReadOnlyList<SmtExpression>? m_asked;

    #endregion

    #region Constructors

    /// <summary>
    /// Looks at the answer of <c>get-value</c> a model came with.
    /// </summary>
    /// <param name="model">The model, with a readable answer of <c>get-model</c>.</param>
    /// <param name="asked">The terms whose values were asked, in order; null when the caller does not say, and each pair is then held to the term it names itself.</param>
    public SmtReportedValues(SmtModel model, IReadOnlyList<SmtExpression>? asked)
    {
        m_model = model;
        m_asked = asked;
        AreTaken = model.ValuesReadable && IsAnswerOf(model, asked);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Holds every pair to the model, in order, until one is found to be another value or a
    /// budget ends the evaluation.
    /// </summary>
    /// <param name="run">The validation, made with the values taken.</param>
    public void Check(SmtRun run)
    {
        var meter = run.Meter;
        for (var index = 0; index < m_model.Reported.Count; index++)
        {
            var (echoed, reported) = m_model.Reported[index];
            var term = m_asked == null ? echoed : m_asked[index];

            // Whether the pair rests on a symbol the model left out: the count of such readings
            // only grows, also through an exception a connective went on past.
            var completions = meter.Completions;
            try
            {
                var value = run.Evaluator.Eval(term, null, 1);

                // From here on only the value is read: a symbol of the problem read now is one the value is written with.
                var references = meter.References;
                var printed = run.Evaluator.Eval(reported, new SmtScope(true), 1);
                var same = Completes(run, term, reported) || run.Equality.Same(value, printed, 1);

                if (meter.Completions != completions)
                {
                    Note(SmtValidationReasons.COMPLETED_SYMBOL, null);
                }
                else if (!same)
                {
                    MismatchAt = index + 1;
                    return;
                }
                else if (meter.References != references)
                {
                    Note(SmtValidationReasons.UNREADABLE_VALUES, null);
                }
            }
            catch (SmtPartialException exception)
            {
                Note(exception.Reason, exception.Detail);

                // A budget ends the validation; so does an exception the budget cannot pay for.
                if (exception.Kind == SmtPartialKind.Terminal || !meter.TryChargeCaught(exception, 0))
                    return;
            }
        }
    }

    /// <summary>
    /// Whether the answer is the answer of what was asked: as many pairs as terms, each pair's
    /// term written as the term asked at its place.
    /// </summary>
    private static bool IsAnswerOf(SmtModel model, IReadOnlyList<SmtExpression>? asked)
    {
        if (asked == null)
            return true;

        if (model.Reported.Count != asked.Count)
            return false;

        for (var index = 0; index < asked.Count; index++)
        {
            if (!model.Reported[index].Term.Is(asked[index]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a pair is the one a constant takes its value from: the constant is declared by
    /// the problem, left out by the answer of <c>get-model</c>, and this is its first value.
    /// </summary>
    private bool Completes(SmtRun run, SmtExpression term, SmtExpression reported)
    {
        return SmtTerms.IsSymbol(term)
               && run.Problem.Symbols.TryGetValue(term.Text, out var symbol)
               && symbol.Kind == SmtSymbolKind.Declared
               && symbol.ParameterSorts!.Count == 0
               && !m_model.Functions.ContainsKey(term.Text)
               && m_model.Completions.TryGetValue(term.Text, out var first)
               && ReferenceEquals(first, reported);
    }

    private void Note(string reason, string? detail)
    {
        if (Reason != null)
            return;

        Reason = reason;
        Detail = detail;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Whether the answer is readable and is the answer of what was asked. An answer that is
    /// not is used for nothing: no pair is checked, and no value completes the model.
    /// </summary>
    public bool AreTaken { get; }

    /// <summary>The 1-based index of the first value that is not its term's, with nothing guessed; 0 when there is none.</summary>
    public int MismatchAt { get; private set; }

    /// <summary>Why a pair before that one, or any pair when there is none, is not decided: the first reason met; null when every pair read is its term's value.</summary>
    public string? Reason { get; private set; }

    /// <summary>The detail that goes with the reason, or null.</summary>
    public string? Detail { get; private set; }

    #endregion
}
