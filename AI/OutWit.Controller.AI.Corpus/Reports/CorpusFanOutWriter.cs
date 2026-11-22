using System.Text.Json;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// Writes what both documents of a run say of its fan-outs, in the same words:
/// <list type="bullet">
/// <item><c>sampling</c>: the share of the packages the run asked to run on two machines
/// (<c>rate</c>; null when the script did not hand the run's options over), how many packages
/// that share comes to (<c>asked_twice</c>), how many came back with a second answer
/// (<c>answered_twice</c>), how many shards had two results that were compared
/// (<c>compared</c>), and the account of it in a word (<c>state</c>: one of
/// <see cref="CorpusSampling.STATES"/>) and in a sentence (<c>statement</c>).</item>
/// <item><c>rounds</c>: for the first round, the repair round, the dispute round and the second
/// dispute round, the task packages that went out and the answers that came back, the packages
/// counted by the answers each got: none (<c>unanswered</c>), one, two, three, more.</item>
/// </list>
/// A run whose checking did not happen says so here, and a round that came back short shows as
/// one: the numbers need not be held against the options the run was started with. The counts
/// are of packages and answers; nothing in them says which machine answered what.
/// </summary>
internal static class CorpusFanOutWriter
{
    #region Constants

    /// <summary>The name of the first round in the documents.</summary>
    public const string ROUND_FIRST = "first";

    /// <summary>The name of the repair round in the documents.</summary>
    public const string ROUND_REPAIR = "repair";

    /// <summary>The name of the dispute round in the documents.</summary>
    public const string ROUND_DISPUTE = "dispute";

    /// <summary>The name of the second dispute round in the documents: the disputes the first left with too few copies, once more.</summary>
    public const string ROUND_DISPUTE_AGAIN = "dispute_again";

    #endregion

    #region Functions

    /// <summary>Writes the <c>sampling</c> and the <c>rounds</c> objects of a run.</summary>
    /// <param name="writer">The document's writer, inside the root object.</param>
    /// <param name="ledger">The server's account of the run.</param>
    public static void Write(Utf8JsonWriter writer, CorpusLedger ledger)
    {
        var sampling = ledger.Sampling;

        writer.WriteStartObject("sampling");
        if (sampling.Rate is { } rate)
            writer.WriteNumber("rate", rate);
        else
            writer.WriteNull("rate");

        writer.WriteString("state", sampling.State);
        writer.WriteNumber("packages", sampling.Packages);
        writer.WriteNumber("asked_twice", sampling.AskedTwice);
        writer.WriteNumber("answered_twice", sampling.AnsweredTwice);
        writer.WriteNumber("compared", sampling.Compared);
        writer.WriteString("statement", sampling.Statement);
        writer.WriteEndObject();

        writer.WriteStartObject("rounds");
        WriteRound(writer, ROUND_FIRST, ledger.FirstRound);
        WriteRound(writer, ROUND_REPAIR, ledger.RepairRound);
        WriteRound(writer, ROUND_DISPUTE, ledger.DisputeRound);
        WriteRound(writer, ROUND_DISPUTE_AGAIN, ledger.DisputeAgainRound);
        writer.WriteEndObject();
    }

    #endregion

    #region Tools

    private static void WriteRound(Utf8JsonWriter writer, string name, CorpusRoundAnswers round)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("packages", round.Packages);
        writer.WriteNumber("answers", round.Answers);
        writer.WriteNumber("unanswered", round.Unanswered);
        writer.WriteNumber("answered_once", round.Once);
        writer.WriteNumber("answered_twice", round.Twice);
        writer.WriteNumber("answered_thrice", round.Thrice);
        writer.WriteNumber("answered_more", round.More);
        writer.WriteEndObject();
    }

    #endregion
}
