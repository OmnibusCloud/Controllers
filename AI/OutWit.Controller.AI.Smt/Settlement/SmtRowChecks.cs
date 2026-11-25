using OutWit.Controller.AI.Smt.Diagnostics;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// Whether a row is one a node of this controller may give for a problem of a package. A node's
/// row is a claim, and the host takes it only when a session of this controller could have
/// written it, as far as that can be checked without the solver:
/// <list type="bullet">
/// <item>it answers the package it came under (its ticket) and a problem of it (its index, with
/// the problem's id, hash and query);</item>
/// <item>it was made by this controller version on one of the platforms the controller ships
/// for;</item>
/// <item>every word is a word of a row, and its texts are there, within what a session writes,
/// and text (<see cref="SmtRowTexts"/>);</item>
/// <item>its words and its counts of models and values go together as a session's outcome has
/// them (<see cref="SmtRowWords"/>);</item>
/// <item>its numbers are within the envelope the package's options give a session
/// (<see cref="SmtRowCosts"/>);</item>
/// <item>and its digest is the digest of its own content.</item>
/// </list>
/// The texts are held first and the digest last: the digest writes the whole row out in memory,
/// so nothing a session could not have written gets that far. Nothing here compares a row with
/// another: copies are compared by their digests only.
/// </summary>
internal static class SmtRowChecks
{
    #region Functions

    /// <summary>Whether a row may be taken as a node's answer to a problem of a package.</summary>
    /// <param name="row">The row.</param>
    /// <param name="ticket">The ticket of the package it came under.</param>
    /// <param name="bounds">What the package's problems allow, and the envelope of its sessions.</param>
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

        if (!SmtRowTexts.Hold(row) || !SmtRowWords.Hold(row, bound) || !SmtRowCosts.Hold(row, bound, bounds.Envelope))
            return false;

        return SmtResultDigest.Holds(row);
    }

    #endregion

    #region Tools

    private static bool IsWord(string? word, IReadOnlyList<string> words)
    {
        return word != null && words.Contains(word, StringComparer.Ordinal);
    }

    #endregion
}
