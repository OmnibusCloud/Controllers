using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The host evaluator's checks of the copies of one problem, a digest at a time. Every copy that
/// says <c>sat</c> is checked under the budget of the result package it came in
/// (<see cref="SmtPackageBudget"/>), and each copy's package pays for its models: copies of one
/// digest are one text, so the row is read from the spool once and each model is checked once for
/// all of them. A digest's check is what the walk that says the most of it gives
/// (<see cref="SmtWalk.Better"/>): a copy whose package has run out does not leave a row partial
/// that another package checked to its end. Nothing is checked before it is asked for, and a
/// digest is checked once.
/// </summary>
internal sealed class SmtProblemChecks
{
    #region Fields

    private readonly SmtHostEvaluator? m_evaluator;
    private readonly IEnumerable<SmtCopy> m_copies;
    private readonly SmtSpool m_spool;
    private readonly long m_packageSteps;
    private readonly CancellationToken m_cancellation;
    private readonly Dictionary<SmtDigestKey, SmtWalk> m_walks = new();

    #endregion

    #region Constructors

    /// <summary>The checks of one problem's copies.</summary>
    /// <param name="evaluator">The evaluator of the problem; null when the host's sanitizer does not take the problem's text.</param>
    /// <param name="copies">Every copy of the problem the checks are asked about.</param>
    /// <param name="spool">The spool the copies' rows were kept in.</param>
    /// <param name="packageSteps">What the models of one result package may cost.</param>
    /// <param name="cancellation">The job's cancellation, looked at before every model.</param>
    public SmtProblemChecks(SmtHostEvaluator? evaluator, IEnumerable<SmtCopy> copies, SmtSpool spool, long packageSteps, CancellationToken cancellation)
    {
        m_evaluator = evaluator;
        m_copies = copies;
        m_spool = spool;
        m_packageSteps = packageSteps;
        m_cancellation = cancellation;
    }

    #endregion

    #region Functions

    /// <summary>The check of a copy: the check of its digest; none for a copy that does not say sat.</summary>
    /// <param name="copy">A copy of the problem.</param>
    /// <returns>The check.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public SmtModelCheck Check(SmtCopy copy)
    {
        return copy.ClaimsSat ? Walk(copy.Digest).Check : SmtModelCheck.NONE;
    }

    /// <summary>The walk that says the most of a digest: every copy of it that says sat, each under its package's budget.</summary>
    /// <param name="digest">The digest of a copy that says sat.</param>
    /// <returns>The walk.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public SmtWalk Walk(SmtDigestKey digest)
    {
        if (m_walks.TryGetValue(digest, out var known))
            return known;

        SmtWalk? best = null;
        SmtResultData? row = null;
        var outcomes = new List<SmtModelOutcome>();
        foreach (var copy in m_copies)
        {
            if (!copy.ClaimsSat || copy.Digest != digest)
                continue;

            if (m_evaluator == null)
            {
                best = new SmtWalk(new SmtModelCheck(SmtRunWords.PARTIAL, SmtValidationReasons.ILL_FORMED_COMMAND, ""), 0, true);
                break;
            }

            row ??= m_spool.Read(copy);
            best = SmtWalk.Better(best, m_evaluator.Walk(row, copy.Budget, m_packageSteps, outcomes, m_cancellation));
        }

        best ??= new SmtWalk(SmtModelCheck.NONE, 0, true);
        m_walks[digest] = best;
        return best;
    }

    #endregion
}
