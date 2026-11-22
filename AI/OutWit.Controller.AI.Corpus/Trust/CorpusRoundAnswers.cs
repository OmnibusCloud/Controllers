namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// How a round's fan-out came back: its task packages, counted by the answers each one got under
/// its ticket. The server knows this before a single answer is opened, and it is what tells a
/// round that came back whole from one that came back short: a package nobody answered, a
/// package that went out on two machines and came back from one. An answer is counted whatever
/// it turns out to hold (results, a fault of its node, nothing readable); what the answers were
/// worth is the ledger's to say.
/// </summary>
/// <param name="Packages">The task packages of the round.</param>
/// <param name="Answers">The answers that came back under their tickets, all together.</param>
/// <param name="Unanswered">The packages no answer came back for.</param>
/// <param name="Once">The packages one answer came back for.</param>
/// <param name="Twice">The packages two answers came back for.</param>
/// <param name="Thrice">The packages three answers came back for.</param>
/// <param name="More">The packages more than three answers came back for.</param>
public sealed record CorpusRoundAnswers(int Packages, int Answers, int Unanswered, int Once, int Twice, int Thrice, int More)
{
    #region Constants

    /// <summary>A round that was not run: no package, no answer.</summary>
    public static readonly CorpusRoundAnswers NONE = new(0, 0, 0, 0, 0, 0, 0);

    #endregion

    #region Functions

    /// <summary>Counts a round's packages by their answers.</summary>
    /// <param name="returned">For each task package of the round, the answers bound to its ticket.</param>
    /// <returns>The counts.</returns>
    public static CorpusRoundAnswers Of(IEnumerable<int> returned)
    {
        int packages = 0, answers = 0, unanswered = 0, once = 0, twice = 0, thrice = 0, more = 0;
        foreach (var count in returned)
        {
            packages++;
            answers += Math.Max(0, count);

            switch (count)
            {
                case <= 0:
                    unanswered++;
                    break;
                case 1:
                    once++;
                    break;
                case 2:
                    twice++;
                    break;
                case 3:
                    thrice++;
                    break;
                default:
                    more++;
                    break;
            }
        }

        return new CorpusRoundAnswers(packages, answers, unanswered, once, twice, thrice, more);
    }

    #endregion

    #region Properties

    /// <summary>The packages two answers or more came back for: the ones a second machine answered.</summary>
    public int TwiceOrMore => Twice + Thrice + More;

    /// <summary>The packages three answers or more came back for.</summary>
    public int ThriceOrMore => Thrice + More;

    #endregion
}
