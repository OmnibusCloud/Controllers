using System.Text.Json;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What a run asked for when it asked for a share of its packages to be solved twice, and
/// whether it got it. The share is the run's sampling rate; which packages it falls on is the
/// platform's choice, made inside the fan-out, and the host never learns it. What the host can
/// count is how many packages a rate asks for, which packages it knows were asked for twice (two
/// results came back for them, or the rate asks for every package), and whether every
/// problem of those ended with two answers of one fan-out, the first or the repair round that
/// sent it out again; that is what the documents state: a run whose checking did not happen says so.
/// </summary>
internal static class SmtSampling
{
    #region Constants

    /// <summary>The script did not tell the settlement what it asked for.</summary>
    public const string NOT_STATED = "not_stated";

    /// <summary>The run asked for no package to be solved twice.</summary>
    public const string NONE = "none";

    /// <summary>
    /// Every problem with an answer of the packages asked for twice ended with two answers of one
    /// fan-out (the first, or the repair round that sent it out again), and the host knows of as
    /// many such packages as the rate asks for.
    /// </summary>
    public const string AS_ASKED = "as_asked";

    /// <summary>
    /// A problem of a package asked for twice rests on fewer than two answers of one fan-out (the
    /// repair round lost a copy again), or fewer packages came back from the first fan-out with two
    /// results than the rate asks for (a package asked for twice that left at most one result cannot
    /// be found to be sent again).
    /// </summary>
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
    /// How many packages of a fan-out a rate asks to be solved twice: the share rounded up to a
    /// whole package, one at least for any rate above 0. It is the rule Grid.Replicas selects by;
    /// the tests run honest fan-outs at several rates and hold this count to what the platform did.
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

    /// <summary>The state of a run's sampling, from what the first fan-out was asked and what the problems of its packages ended with. A dispute round is not sampling.</summary>
    /// <param name="rate">The rate the script stated, or null when it stated none.</param>
    /// <param name="selected">The packages of the first fan-out the rate asks for twice (<see cref="Selected"/>).</param>
    /// <param name="known">The packages the host knows were asked for twice: two results or more came back for them, or the rate asks for every package.</param>
    /// <param name="shortProblems">The problems with an answer of those packages that ended without two answers of one fan-out.</param>
    /// <returns>One of <see cref="STATES"/>.</returns>
    public static string State(double? rate, int selected, int known, int shortProblems)
    {
        if (rate == null)
            return NOT_STATED;

        if (!(rate > 0))
            return NONE;

        return known < selected || shortProblems > 0 ? SHORT : AS_ASKED;
    }

    /// <summary>Writes the rate (null when the script did not state it), the state in a fixed word, and the packages nothing answered.</summary>
    /// <param name="writer">The document's writer, inside an object.</param>
    /// <param name="rate">The sampling rate the script stated, or null.</param>
    /// <param name="state">The state of the sampling, one of <see cref="STATES"/>.</param>
    /// <param name="unanswered">The packages of problems no taken answer came back for, every fan-out together.</param>
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
