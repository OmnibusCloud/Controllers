using System.Globalization;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// What a run asked for in second executions and what came back of it, in numbers and in a word.
/// The run's options ask for a share of the first round's packages to run on two machines; the
/// platform picks them and does not say which, so the server cannot name the packages that were
/// picked. It can count: how many the share comes to, how many packages a second answer came back
/// for, and how many shards had two results that were compared. A first round that came back
/// short — every answer lost, a second machine that returned nothing — shows here, in
/// <see cref="State"/> and in <see cref="Statement"/>, and not only as numbers a reader has to
/// hold against the options the run was started with.
/// </summary>
/// <param name="Rate">The share of the packages the run asked to run twice, 0 to 1; null when the activity that writes the document was not handed the run's options.</param>
/// <param name="Packages">The task packages of the first round.</param>
/// <param name="AskedTwice">The packages that share comes to by the platform's rule (rounded up, one at the least when the share is above 0); 0 when the rate is 0 or not known.</param>
/// <param name="AnsweredTwice">The packages two answers or more came back for.</param>
/// <param name="Compared">The shards whose first two values of the first round were both results, and were compared.</param>
public sealed record CorpusSampling(double? Rate, int Packages, int AskedTwice, int AnsweredTwice, int Compared)
{
    #region Constants

    /// <summary>The run asked for no second execution: nothing was to be compared.</summary>
    public const string STATE_NOT_ASKED = "not_asked";

    /// <summary>As many packages came back twice as the run asked for, and shards were compared.</summary>
    public const string STATE_AS_ASKED = "as_asked";

    /// <summary>Fewer packages came back twice than the run asked for: some of the checking did not happen.</summary>
    public const string STATE_SHORT = "short";

    /// <summary>The run asked for second executions and no shard was compared: the checking did not happen.</summary>
    public const string STATE_NONE = "none";

    /// <summary>The rate the run asked for is not known to the activity that wrote the document.</summary>
    public const string STATE_UNKNOWN = "unknown";

    /// <summary>Every state, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> STATES = [STATE_NOT_ASKED, STATE_AS_ASKED, STATE_SHORT, STATE_NONE, STATE_UNKNOWN];

    #endregion

    #region Functions

    /// <summary>The share of the packages a run's options ask to run twice: <see cref="CorpusOptionsData.SamplingRate"/> held to 0..1.</summary>
    /// <param name="options">The run's options; null = none, which asks for nothing.</param>
    /// <returns>0 to 1.</returns>
    public static double RateOf(CorpusOptionsData? options)
    {
        var requested = options?.SamplingRate ?? 0;

        // Not a number asks for nothing.
        return requested > 0 ? Math.Min(requested, 1) : 0;
    }

    /// <summary>
    /// The share of the repair round's packages that run twice: all of them when the run asked
    /// for sampling at all, none when it asked for none. A shard goes to the repair round because
    /// nothing usable came back for it, and nothing says whether it had been picked for a second
    /// machine; so the round keeps the run's checking only if every shard of it goes out twice.
    /// </summary>
    /// <param name="options">The run's options; null = none, which asks for nothing.</param>
    /// <returns>1 or 0.</returns>
    public static double RepairRateOf(CorpusOptionsData? options)
    {
        return RateOf(options) > 0 ? 1 : 0;
    }

    /// <summary>What a first round's answers say of the sampling a run asked for.</summary>
    /// <param name="rate">The share the run asked for; null when it is not known.</param>
    /// <param name="first">How the first round's fan-out came back.</param>
    /// <param name="compared">The shards of the first round whose two results were compared.</param>
    /// <returns>The account.</returns>
    public static CorpusSampling Of(double? rate, CorpusRoundAnswers first, int compared)
    {
        var asked = rate is { } share ? CorpusStorageEstimator.SampledShards(first.Packages, share) : 0;

        return new CorpusSampling(rate, first.Packages, asked, first.TwiceOrMore, compared);
    }

    #endregion

    #region Properties

    /// <summary>The account in a word: one of <see cref="STATES"/>.</summary>
    public string State => Rate == null ? STATE_UNKNOWN
        : AskedTwice == 0 ? STATE_NOT_ASKED
        : Compared == 0 ? STATE_NONE
        : AnsweredTwice < AskedTwice ? STATE_SHORT
        : STATE_AS_ASKED;

    /// <summary>The account in a sentence, for a person who reads the document: fixed words and the numbers beside them.</summary>
    public string Statement => State switch
    {
        STATE_UNKNOWN => "The sampling the run asked for is not known here: the script did not hand the options of the run to this activity.",
        STATE_NOT_ASKED => "The run asked for no second execution: every shard of the first round rests on one machine.",
        STATE_NONE => string.Create(CultureInfo.InvariantCulture, $"The run asked for {AskedTwice} of its {Packages} package(s) to run on two machines, {AnsweredTwice} came back twice and no shard was compared: the checking the run asked for did not happen in the first round."),
        STATE_SHORT => string.Create(CultureInfo.InvariantCulture, $"The run asked for {AskedTwice} of its {Packages} package(s) to run on two machines and {AnsweredTwice} came back twice: the checking of the first round is short of what the run asked for."),
        _ => string.Create(CultureInfo.InvariantCulture, $"The run asked for {AskedTwice} of its {Packages} package(s) to run on two machines and {AnsweredTwice} came back twice.")
    };

    #endregion
}
