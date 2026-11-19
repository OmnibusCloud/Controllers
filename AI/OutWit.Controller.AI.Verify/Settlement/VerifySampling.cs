using System.Text.Json;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// What a run asked for when it asked for a share of its packages to run twice, and whether it
/// got it. The share is the run's sampling rate; which packages it falls on is the platform's
/// choice, made at random inside the fan-out, and the server never learns it. What the server
/// can count is how many packages a rate asks for and how many came back with their two
/// answers, and that is what the documents state: a run whose checking did not happen says so.
/// </summary>
internal static class VerifySampling
{
    #region Constants

    /// <summary>The script did not tell the settlement what it asked for: nothing is known to be missing or done.</summary>
    public const string NOT_STATED = VerifySettlementSummary.SAMPLING_NOT_STATED;

    /// <summary>The run asked for no package to run twice.</summary>
    public const string NONE = "none";

    /// <summary>As many packages came back with two accepted answers as the rate asks for, in the first fan-out and in the repair round.</summary>
    public const string AS_ASKED = "as_asked";

    /// <summary>Fewer packages came back with two accepted answers than the rate asks for: part of the checking, or all of it, did not happen.</summary>
    public const string SHORT = "short";

    /// <summary>Every state, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> STATES = [NOT_STATED, NONE, AS_ASKED, SHORT];

    // The platform's own allowance for a share that is a whole number of packages but for rounding.
    private const double SHARE_ROUNDING_TOLERANCE = 1E-09;

    #endregion

    #region Functions

    /// <summary>A rate as the fan-out takes it: 0 to 1, and 0 for anything that is no share.</summary>
    /// <param name="rate">The rate a script handed over.</param>
    /// <returns>The rate, 0 to 1.</returns>
    public static double Clamp(double rate)
    {
        return rate > 0 ? Math.Min(rate, 1) : 0;
    }

    /// <summary>
    /// How many packages of a fan-out a rate asks to run twice: the share rounded up to a whole
    /// package, one at least for any rate above 0. It is the rule Grid.Replicas selects by; the
    /// tests run honest fan-outs at several rates and hold this count to what the platform did.
    /// </summary>
    /// <param name="packages">The packages the fan-out gave out.</param>
    /// <param name="rate">The sampling rate, 0 to 1.</param>
    /// <returns>The packages asked for twice; 0 for a rate of 0 or a fan-out of nothing.</returns>
    public static int Selected(int packages, double rate)
    {
        if (packages <= 0 || !(rate > 0))
            return 0;

        var selected = (int)Math.Ceiling(packages * Math.Min(rate, 1.0) - SHARE_ROUNDING_TOLERANCE);
        return Math.Clamp(selected, 1, packages);
    }

    /// <summary>
    /// The state of a run's sampling, from what its fan-outs were asked and what came back. A
    /// dispute round is not sampling: how it ended is in the record of each dispute.
    /// </summary>
    /// <param name="rate">The rate the script stated, or null when it stated none.</param>
    /// <param name="fanOuts">The run's fan-outs.</param>
    /// <returns>One of <see cref="STATES"/>.</returns>
    public static string State(double? rate, IEnumerable<VerifyFanOutRecord> fanOuts)
    {
        if (rate == null)
            return NOT_STATED;

        if (!(rate > 0))
            return NONE;

        return fanOuts.Any(fanOut => fanOut.Round != VerifyRound.DISPUTE && fanOut.IsShort) ? SHORT : AS_ASKED;
    }

    /// <summary>
    /// Writes what a run asked for and got of its sampling: the same three fields in the summary
    /// of the report and of the integrity document. The rate (null when the script did not state
    /// it), the state in a fixed word, and the packages nothing answered.
    /// </summary>
    /// <param name="writer">The document's writer, inside a summary.</param>
    /// <param name="rate">The sampling rate the script stated, or null.</param>
    /// <param name="state">The state of the sampling, one of <see cref="STATES"/>.</param>
    /// <param name="unanswered">The task packages no accepted answer came back for, every fan-out together.</param>
    public static void Write(Utf8JsonWriter writer, double? rate, string state, int unanswered)
    {
        if (rate is { } stated)
            writer.WriteNumber("sampling_rate", stated);
        else
            writer.WriteNull("sampling_rate");

        writer.WriteString("sampling", state);
        writer.WriteNumber("unanswered_packages", unanswered);
    }

    #endregion
}
