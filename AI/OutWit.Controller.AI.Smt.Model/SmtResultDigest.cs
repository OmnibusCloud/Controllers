using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// The canonical form of a row (<see cref="SmtResultData"/>) and its SHA-256, the row's
/// <see cref="SmtResultData.Digest"/>: what copies of a problem solved on two machines are
/// compared by. Two honest machines give the same digest for a problem; a host recomputes it
/// from the row it received rather than trusting the one the row carries.
/// <para>
/// The canonical form is every value of the row that is a function of the problem, the run's
/// options and the solver module, one after the other, each with its name and, for a text, its
/// length, so that no two rows write the same form: the index, the id, the hash, the query, the
/// status, the code, the message, the verdict, the reason and the solver's word for it, the proof,
/// the count, completeness, the limit, every model, every value, the checks, the resource count,
/// the fuel, the peak memory and the transcript. Left out: the wall time and the platform, which
/// are the machine's; the controller version, which is the job's; the ticket, which is the
/// package's (a problem sent again in another package keeps its digest); and the digest itself.
/// </para>
/// <para>
/// What is hashed is the UTF-8 of the canonical form, and only of a form that has one: a text
/// with half of a surrogate pair alone is no text, and no character is ever put in its place (the
/// row would then share its digest with the row that holds that character there). Such a row has
/// no digest: <see cref="Of"/> gives <see cref="NONE"/> and <see cref="Holds"/> is false, without
/// an exception. A node's session never writes such a text, so a row a node made has its digest.
/// </para>
/// </summary>
public static class SmtResultDigest
{
    #region Constants

    /// <summary>The first line of the canonical form: its version.</summary>
    public const string FORMAT = "smt-result/1";

    /// <summary>What <see cref="Of"/> gives for a row that has no digest: the empty text, which is no SHA-256.</summary>
    public const string NONE = "";

    private const char LINE_FEED = '\n';

    // Strict: a character that has no UTF-8 is refused, never replaced by another.
    private static readonly UTF8Encoding TEXT = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    #endregion

    #region Functions

    /// <summary>
    /// The SHA-256 of a row's canonical form.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>Lower-case hex; <see cref="NONE"/> for a row one of whose texts is not valid UTF-16 (half of a surrogate pair alone), which has no digest.</returns>
    /// <exception cref="ArgumentNullException">There is no row.</exception>
    public static string Of(SmtResultData row)
    {
        var canonical = Canonical(row);

        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(TEXT.GetBytes(canonical)));
        }
        catch (EncoderFallbackException)
        {
            return NONE;
        }
    }

    /// <summary>
    /// Whether a row carries the digest of its own values.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>True when <see cref="SmtResultData.Digest"/> is the digest of the row's canonical form; false for a row that has no digest, whatever it carries.</returns>
    /// <exception cref="ArgumentNullException">There is no row.</exception>
    public static bool Holds(SmtResultData row)
    {
        var digest = Of(row);
        return digest.Length > 0 && string.Equals(row.Digest, digest, StringComparison.Ordinal);
    }

    /// <summary>
    /// The canonical form of a row: its first line <see cref="FORMAT"/>, then a line a value,
    /// <c>name=value</c> for a word or a number and <c>name[length]=text</c> for a text, where the
    /// length counts UTF-16 characters and the text may hold line feeds of its own.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentNullException">There is no row.</exception>
    public static string Canonical(SmtResultData row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var builder = new StringBuilder();
        builder.Append(FORMAT).Append(LINE_FEED);

        Word(builder, "index", Number(row.Index));
        Text(builder, "id", row.Id);
        Text(builder, "hash", row.Hash);
        Word(builder, "query", Query(row.Query));
        Text(builder, "status", row.Status);
        Text(builder, "code", row.Code);
        Text(builder, "message", row.Message);
        Text(builder, "verdict", row.Verdict);
        Text(builder, "reason", row.Reason);
        Text(builder, "reason_text", row.ReasonText);
        Text(builder, "proof", row.Proof);
        Word(builder, "count", Number(row.Count));
        Word(builder, "complete", row.Complete ? "true" : "false");
        Word(builder, "limit_reached", row.LimitReached ? "true" : "false");

        var models = row.Models ?? [];
        Word(builder, "models", Number(models.Count));
        foreach (var model in models)
            Text(builder, "model", model);

        var values = row.Values ?? [];
        Word(builder, "values", Number(values.Count));
        foreach (var value in values)
            Text(builder, "value", value);

        Word(builder, "checks", Number(row.Checks));
        Word(builder, "resource_count", row.ResourceCount.ToString(CultureInfo.InvariantCulture));
        Word(builder, "fuel", row.Fuel.ToString(CultureInfo.InvariantCulture));
        Word(builder, "peak_memory", row.PeakMemoryBytes.ToString(CultureInfo.InvariantCulture));
        Text(builder, "transcript", row.Transcript);

        return builder.ToString();
    }

    #endregion

    #region Tools

    private static string Query(SmtQuery query)
    {
        return query switch
        {
            SmtQuery.Check => "check",
            SmtQuery.Enumerate => "enumerate",
            SmtQuery.Prove => "prove",
            _ => Number((int)query)
        };
    }

    private static string Number(int number)
    {
        return number.ToString(CultureInfo.InvariantCulture);
    }

    private static void Word(StringBuilder builder, string name, string value)
    {
        builder.Append(name).Append('=').Append(value).Append(LINE_FEED);
    }

    private static void Text(StringBuilder builder, string name, string? value)
    {
        value ??= "";
        builder.Append(name).Append('[').Append(Number(value.Length)).Append("]=").Append(value).Append(LINE_FEED);
    }

    #endregion
}
