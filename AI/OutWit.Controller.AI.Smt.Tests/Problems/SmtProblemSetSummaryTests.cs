using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// What a reader has counted of a set: every tally, and that each of them is bounded whatever
/// the set holds.
/// </summary>
[TestFixture]
public class SmtProblemSetSummaryTests
{
    #region Constants

    private static readonly string[] SET =
    [
        """{"id":"a","smt2":"(set-logic QF_LIA)(set-info :license \"CC BY 4.0\")(set-info :status sat)(declare-const x Int)(assert (> x 0))(check-sat)(exit)"}""",
        """{"id":"b","smt2":"(set-logic QF_LIA)(set-info :license \"CC BY 4.0\")(declare-const x Int)(assert (> x 0))(check-sat)(get-model)","query":"enumerate","project":["x"],"expected":"unsat"}""",
        "",
        """{"id":"c","smt2":"(set-logic QF_BV)(set-info :license |MIT|)(declare-const p Bool)(assert p)","query":"prove","goal":"p"}""",
        """{"id":"d","smt2":"(assert true)"}""",
        "not json",
        """{"id":"a","smt2":"(assert true)"}""",
        """{"id":"e","smt2":"(declare-const x Int)\n(push 1)"}""",
        """{"id":"f","smt2":"(assert true)","nope":1}""",
        """{"id":"g","smt2":"(assert true)","query":"prove"}"""
    ];

    #endregion

    #region Tally Tests

    [Test]
    public void SummaryCountsEveryRowInEveryTallyTest()
    {
        var text = string.Join("\n", SET) + "\n";

        var summary = SmtProblemSets.Read(text).Summary;

        Assert.That((summary.Rows, summary.Accepted, summary.Refused), Is.EqualTo((9, 4, 5)));
        Assert.That(summary.BytesRead, Is.EqualTo(Encoding.UTF8.GetByteCount(text)));
        Assert.That(summary.Logics, Is.EquivalentTo(new Dictionary<string, int> { ["QF_LIA"] = 2, ["QF_BV"] = 1, [""] = 1 }));
        Assert.That(summary.Queries, Is.EquivalentTo(new Dictionary<SmtQuery, int> { [SmtQuery.Check] = 2, [SmtQuery.Enumerate] = 1, [SmtQuery.Prove] = 1 }));
        Assert.That(summary.Expected, Is.EquivalentTo(new Dictionary<SmtProblemStatus, int> { [SmtProblemStatus.Sat] = 1, [SmtProblemStatus.Unsat] = 1, [SmtProblemStatus.None] = 2 }));
        Assert.That(summary.Stripped, Is.EquivalentTo(new Dictionary<string, int> { ["check-sat"] = 2, ["exit"] = 1, ["get-model"] = 1 }));
        Assert.That(summary.Licenses, Is.EquivalentTo(new Dictionary<string, int> { ["CC BY 4.0"] = 2, ["MIT"] = 1, [""] = 1 }));
        Assert.That((summary.OtherLogics, summary.OtherLicenses), Is.EqualTo((0, 0)));
        Assert.That(summary.RefusalCodes, Is.EquivalentTo(new Dictionary<string, int>
        {
            [SmtRefusalCodes.ROW_MALFORMED] = 1, [SmtRefusalCodes.ID_DUPLICATE] = 1, [SmtRefusalCodes.COMMAND_NOT_ALLOWED] = 1,
            [SmtRefusalCodes.FIELD_NOT_ALLOWED] = 1, [SmtRefusalCodes.FIELD_MISSING] = 1
        }));
    }

    [Test]
    public void SummaryKeepsTheRefusalsInFullInTheOrderOfTheSetTest()
    {
        var summary = SmtProblemSets.Read(string.Join("\n", SET)).Summary;

        Assert.That(summary.Refusals.Select(refusal => (refusal.Row, refusal.File, refusal.Line, refusal.Command, refusal.Code)), Is.EqualTo(new[]
        {
            (6, "", 0, "", SmtRefusalCodes.ROW_MALFORMED),
            (7, "a", 0, "", SmtRefusalCodes.ID_DUPLICATE),
            (8, "e", 2, "push", SmtRefusalCodes.COMMAND_NOT_ALLOWED),
            (9, "f", 0, "", SmtRefusalCodes.FIELD_NOT_ALLOWED),
            (10, "g", 0, "", SmtRefusalCodes.FIELD_MISSING)
        }));
        Assert.That(summary.Refusals.Select(refusal => refusal.Reason), Is.All.Not.Empty);
    }

    [Test]
    public void SummaryIsKeptWhileTheSetIsReadTest()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", SET)));
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var seen = new List<(int Rows, int Accepted, int Refused)>();

        Assert.That((reader.Summary.Rows, reader.Summary.BytesRead), Is.EqualTo((0, 0L)), "nothing before the first row");
        while (reader.TryRead(out _))
            seen.Add((reader.Summary.Rows, reader.Summary.Accepted, reader.Summary.Refused));

        Assert.That(seen, Is.EqualTo(new[] { (1, 1, 0), (2, 2, 0), (3, 3, 0), (4, 4, 0), (5, 4, 1), (6, 4, 2), (7, 4, 3), (8, 4, 4), (9, 4, 5) }));
    }

    [Test]
    public void SummaryCannotBeChangedFromOutsideTest()
    {
        var (rows, summary) = SmtProblemSets.Read(string.Join("\n", SET));

        // The record a row carries is the caller's; the summary keeps one of its own.
        var refused = rows.First(row => !row.IsAccepted);
        refused.Refusal!.Reason = "changed by the caller";
        refused.Refusal.Row = 99;

        Assert.That(summary.Refusals[0].Reason, Is.EqualTo("the line is not one JSON object"));
        Assert.That(summary.Refusals[0].Row, Is.EqualTo(6));

        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)summary.Logics).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<SmtQuery, int>)summary.Queries).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<SmtProblemStatus, int>)summary.Expected).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)summary.Stripped).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)summary.RefusalCodes).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)summary.Licenses).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<SmtRefusalData>)summary.Refusals).Clear());
    }

    #endregion

    #region Bound Tests

    [Test]
    public void SummaryKeepsTheFirstThousandRefusalsAndCountsTheRestTest()
    {
        var lines = new StringBuilder();
        for (var index = 0; index < SmtProblemSetLimits.MAX_REFUSALS_KEPT + 250; index++)
            lines.Append("{\"id\":\"r").Append(index).Append("\",\"smt2\":\"(echo \\\"x\\\")\"}\n");

        var summary = SmtProblemSets.Read(lines.ToString()).Summary;

        Assert.That(summary.Refused, Is.EqualTo(1_250));
        Assert.That(summary.RefusalCodes[SmtRefusalCodes.COMMAND_NOT_ALLOWED], Is.EqualTo(1_250));
        Assert.That(summary.Refusals, Has.Count.EqualTo(1_000));
        Assert.That(summary.Refusals.Select(refusal => refusal.File), Is.EqualTo(Enumerable.Range(0, 1_000).Select(index => $"r{index}")), "the first of them, in order");
    }

    [Test]
    public void SummaryKeepsSoManyLicencesByNameAndCountsTheRestTogetherTest()
    {
        // 70 licences, each on two problems, then the first and the last of them once more.
        var lines = new StringBuilder();
        var id = 0;
        foreach (var licence in Enumerable.Range(0, 70).Concat(Enumerable.Range(0, 70)).Append(0).Append(69))
            lines.Append("{\"id\":\"p").Append(id++).Append("\",\"smt2\":\"(set-info :license |licence ").Append(licence).Append("|)(assert true)\"}\n");

        var summary = SmtProblemSets.Read(lines.ToString()).Summary;

        Assert.That(summary.Accepted, Is.EqualTo(142));
        Assert.That(summary.Licenses, Has.Count.EqualTo(SmtProblemSetLimits.MAX_LICENSES));
        Assert.That(summary.Licenses.Keys, Is.EquivalentTo(Enumerable.Range(0, 64).Select(licence => $"licence {licence}")), "the first 64 that were met");
        Assert.That(summary.Licenses["licence 0"], Is.EqualTo(3));
        Assert.That(summary.Licenses["licence 63"], Is.EqualTo(2));
        Assert.That(summary.OtherLicenses, Is.EqualTo(13), "six licences twice, and one of them once more");
        Assert.That(summary.Licenses.Values.Sum() + summary.OtherLicenses, Is.EqualTo(summary.Accepted));
    }

    [Test]
    public void SummaryKeepsSoManyLogicsByNameCutShortTest()
    {
        var lines = new StringBuilder();
        for (var index = 0; index < 80; index++)
            lines.Append("{\"id\":\"p").Append(index).Append("\",\"smt2\":\"(set-logic L").Append(index).Append(")(assert true)\"}\n");

        lines.Append("{\"id\":\"long\",\"smt2\":\"(set-logic ").Append(new string('Q', 5_000)).Append(")(assert true)\"}\n");

        var summary = SmtProblemSets.Read(lines.ToString()).Summary;

        Assert.That(summary.Logics, Has.Count.EqualTo(SmtProblemSetLimits.MAX_LOGICS));
        Assert.That(summary.Logics.Keys, Is.EquivalentTo(Enumerable.Range(0, 64).Select(index => $"L{index}")));
        Assert.That(summary.OtherLogics, Is.EqualTo(17));

        // A logic is a symbol of the lab's: its name is kept as a token is shown, on one line and cut.
        var odd = SmtProblemSets.Read("{\"id\":\"a\",\"smt2\":\"(set-logic |two\\nlines|)\"}\n{\"id\":\"b\",\"smt2\":\"(set-logic " + new string('Q', 5_000) + ")\"}").Summary;
        Assert.That(odd.Logics.Keys, Is.EquivalentTo(new[] { "two lines", new string('Q', 61) + "..." }));
    }

    [Test]
    public void SummaryOfAHostileSetStaysSmallTest()
    {
        // Every row with a licence and a logic of its own, as long as they may be: what is kept
        // of them is bounded by the number of names, not by the number of rows.
        var lines = new StringBuilder();
        for (var index = 0; index < 400; index++)
            lines.Append("{\"id\":\"p").Append(index).Append("\",\"smt2\":\"(set-logic L").Append(index).Append(new string('x', 200)).Append(")(set-info :license |")
                .Append(index).Append(new string('y', 2_000)).Append("|)(assert true)\"}\n");

        var summary = SmtProblemSets.Read(lines.ToString()).Summary;

        Assert.That(summary.Logics.Keys.Sum(name => name.Length), Is.LessThanOrEqualTo(64 * 64));
        Assert.That(summary.Licenses.Keys.Sum(name => name.Length), Is.LessThanOrEqualTo(64 * SmtSanitizer.ATTRIBUTION_CHARACTERS));
        Assert.That((summary.OtherLogics, summary.OtherLicenses), Is.EqualTo((336, 336)));
    }

    #endregion
}
