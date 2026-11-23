using OutWit.Controller.AI.Smt.Diagnostics;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// Whether a row is one a node of this controller may give for a problem of a package. A node's
/// row is a claim, and the host takes it only when everything that can be checked without the
/// solver holds: it answers the package it came under (its ticket) and a problem of it (its index,
/// with the problem's id, hash and query); it was made by this controller version on one of the
/// platforms the controller ships for; every word is a word of a row; its counts of models and
/// values are the ones a session of its query gives; its texts are within what a session writes;
/// and its digest is the digest of its own content. Nothing here compares a row with another:
/// copies are compared by their digests only.
/// </summary>
internal static class SmtRowChecks
{
    #region Constants

    private const int MAX_CODE_CHARACTERS = 64;

    #endregion

    #region Functions

    /// <summary>Whether a row may be taken as a node's answer to a problem of a package.</summary>
    /// <param name="row">The row.</param>
    /// <param name="ticket">The ticket of the package it came under.</param>
    /// <param name="bounds">What the package's problems allow.</param>
    /// <returns>True when the row may be taken.</returns>
    public static bool Holds(SmtResultData row, string ticket, SmtTaskBounds bounds)
    {
        if (!string.Equals(row.Ticket, ticket, StringComparison.Ordinal) || !bounds.TryGet(row.Index, out var bound))
            return false;

        if (!string.Equals(row.Id, bound.Id, StringComparison.Ordinal)
            || !string.Equals(row.Hash, bound.Hash, StringComparison.Ordinal)
            || row.Query != bound.Query)
            return false;

        if (!string.Equals(row.ControllerVersion, ControllerBuildInfo.VERSION, StringComparison.Ordinal) || !SmtCapabilitiesWriter.PLATFORMS.Contains(row.Platform))
            return false;

        if (!IsWord(row.Status, SmtResultWords.STATUSES) || !IsWord(row.Verdict, SmtResultWords.VERDICTS)
            || !IsWord(row.Reason, SmtResultWords.REASONS) || !IsWord(row.Proof, SmtResultWords.PROOFS))
            return false;

        if (row.Proof != ProofOf(row.Query, row.Verdict))
            return false;

        if (!HasItsCounts(row, bound))
            return false;

        if (row.Code is not { Length: <= MAX_CODE_CHARACTERS } || row.Message is not { Length: <= SmtResultWords.MAX_MESSAGE_CHARACTERS }
            || row.ReasonText == null || row.Transcript is not { Length: <= SmtTranscript.LIMIT_BYTES })
            return false;

        if (row.Checks < 0 || row.ResourceCount < 0 || row.Fuel < 0 || row.PeakMemoryBytes < 0 || row.WallMs < 0)
            return false;

        return SmtResultDigest.Holds(row);
    }

    /// <summary>The word of a proof a session of a query gives with a verdict.</summary>
    /// <param name="query">The query.</param>
    /// <param name="verdict">The verdict.</param>
    /// <returns><c>proved</c>, <c>counterexample</c> or <c>none</c>.</returns>
    public static string ProofOf(SmtQuery query, string verdict)
    {
        if (query != SmtQuery.Prove)
            return SmtResultWords.NONE;

        return verdict switch
        {
            SmtResultWords.UNSAT => SmtResultWords.PROVED,
            SmtResultWords.SAT => SmtResultWords.COUNTEREXAMPLE,
            _ => SmtResultWords.NONE
        };
    }

    #endregion

    #region Tools

    private static bool IsWord(string? word, IReadOnlyList<string> words)
    {
        return word != null && words.Contains(word, StringComparer.Ordinal);
    }

    /// <summary>
    /// A session of a check gives at most one model and one answer of values; a proof at most a
    /// model; an enumeration a model and its projected values together, as many as it counted and
    /// no more than it was asked for. A row of any other query has no count.
    /// </summary>
    private static bool HasItsCounts(SmtResultData row, SmtTaskBound bound)
    {
        if (row.Models == null || row.Values == null || row.Models.Any(model => model == null) || row.Values.Any(value => value == null))
            return false;

        return row.Query switch
        {
            SmtQuery.Check => row.Count == 0 && row.Models.Count <= 1 && row.Values.Count <= 1,
            SmtQuery.Prove => row.Count == 0 && row.Models.Count <= 1 && row.Values.Count == 0,
            SmtQuery.Enumerate => row.Count == row.Models.Count && row.Values.Count == row.Models.Count && row.Count <= bound.MaxModels,
            _ => false
        };
    }

    #endregion
}
