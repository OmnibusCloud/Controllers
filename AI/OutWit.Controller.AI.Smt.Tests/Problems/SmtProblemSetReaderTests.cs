using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A problem set as a stream of lines: which lines are rows, how they are numbered, and what a
/// reader does at the start and at the end of a stream.
/// </summary>
[TestFixture]
public class SmtProblemSetReaderTests
{
    #region Constants

    private const string FIRST = """{"id":"first","smt2":"(declare-const p Bool)(assert p)"}""";
    private const string SECOND = """{"id":"second","smt2":"(assert false)","expected":"unsat","tags":"a tag"}""";
    private const string BROKEN = """{"id":"third","smt2":"(push 1)"}""";

    private static readonly byte[] BYTE_ORDER_MARK = [0xEF, 0xBB, 0xBF];

    #endregion

    #region Row Tests

    [Test]
    public void ReaderGivesEveryRowWithItsLineAndItsIndexTest()
    {
        var (rows, summary) = SmtProblemSets.Read($"{FIRST}\n{SECOND}\n{BROKEN}\n");

        Assert.That(rows.Select(row => (row.Line, row.Index, row.IsAccepted)), Is.EqualTo(new[] { (1, 0, true), (2, 1, true), (3, 2, false) }));
        Assert.That(rows[0].Data!.Id, Is.EqualTo("first"));
        Assert.That(rows[0].Data!.Text, Is.EqualTo("(declare-const p Bool)\n(assert p)\n"));
        Assert.That(rows[0].Problem!.Constants.Single().Name, Is.EqualTo("p"));
        Assert.That(rows[1].Data!.Expected, Is.EqualTo(SmtProblemStatus.Unsat));
        Assert.That(rows[1].Data!.Tags, Is.EqualTo("a tag"));
        Assert.That(rows[2].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.COMMAND_NOT_ALLOWED));
        Assert.That((summary.Rows, summary.Accepted, summary.Refused), Is.EqualTo((3, 2, 1)));
    }

    [Test]
    public void BlankLinesAreNoRowsAndStillCountAsLinesTest()
    {
        var (rows, summary) = SmtProblemSets.Read($"\n  \t \n{FIRST}\n\n\r\n \r\n{SECOND}\n\n");

        Assert.That(rows.Select(row => (row.Line, row.Index)), Is.EqualTo(new[] { (3, 0), (7, 1) }));
        Assert.That(summary.Rows, Is.EqualTo(2));
    }

    [Test]
    public void LineEndsAreALineFeedWithOrWithoutACarriageReturnTest()
    {
        var unix = SmtProblemSets.Read($"{FIRST}\n{SECOND}\n").Rows;
        var windows = SmtProblemSets.Read($"{FIRST}\r\n{SECOND}\r\n").Rows;

        Assert.That(windows.Select(row => (row.Line, row.Index, row.Data!.Hash)), Is.EqualTo(unix.Select(row => (row.Line, row.Index, row.Data!.Hash))));
        Assert.That(windows[1].Data!.Is(unix[1].Data!), Is.True);
    }

    [Test]
    public void LastLineNeedsNoLineEndTest()
    {
        var rows = SmtProblemSets.Read($"{FIRST}\n{SECOND}").Rows;

        Assert.That(rows.Select(row => (row.Line, row.IsAccepted)), Is.EqualTo(new[] { (1, true), (2, true) }));
        Assert.That(SmtProblemSets.Read(FIRST).Rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void StreamWithoutRowsIsASetWithoutRowsTest()
    {
        foreach (var text in new[] { "", "\n", "\r\n\r\n", "   \t ", " \n\t\n" })
        {
            var (rows, summary) = SmtProblemSets.Read(text);

            Assert.That(rows, Is.Empty);
            Assert.That((summary.Rows, summary.Accepted, summary.Refused), Is.EqualTo((0, 0, 0)));
        }
    }

    [Test]
    public void ByteOrderMarkAtTheVeryStartIsSkippedTest()
    {
        var marked = BYTE_ORDER_MARK.Concat(Encoding.UTF8.GetBytes($"{FIRST}\n{SECOND}\n")).ToArray();

        var (rows, summary) = SmtProblemSets.Read(marked);

        Assert.That(rows.Select(row => (row.Line, row.IsAccepted)), Is.EqualTo(new[] { (1, true), (2, true) }));
        Assert.That(summary.BytesRead, Is.EqualTo(marked.Length));
        Assert.That(SmtProblemSets.Read(BYTE_ORDER_MARK).Rows, Is.Empty, "a stream that is the mark and nothing else");
    }

    [Test]
    public void ByteOrderMarkAnywhereElseIsPartOfItsLineTest()
    {
        var bytes = Encoding.UTF8.GetBytes(FIRST + "\n").Concat(BYTE_ORDER_MARK).Concat(Encoding.UTF8.GetBytes(SECOND + "\n")).ToArray();

        var rows = SmtProblemSets.Read(bytes).Rows;

        Assert.That(rows[0].IsAccepted, Is.True);
        Assert.That(rows[1].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.ROW_MALFORMED));
        Assert.That(rows[1].Refusal!.Reason, Is.EqualTo("the line is not one JSON object"));
    }

    [Test]
    public void RefusedRowDoesNotStopTheReadingTest()
    {
        var (rows, summary) = SmtProblemSets.Read($"not json\n{FIRST}\n{{\"id\":\n{SECOND}\n[]\n");

        Assert.That(rows.Select(row => row.IsAccepted), Is.EqualTo(new[] { false, true, false, true, false }));
        Assert.That(rows.Where(row => !row.IsAccepted).Select(row => (row.Refusal!.Row, row.Refusal.Code)),
            Is.EqualTo(new[] { (1, SmtRefusalCodes.ROW_MALFORMED), (3, SmtRefusalCodes.ROW_MALFORMED), (5, SmtRefusalCodes.ROW_MALFORMED) }));
        Assert.That((summary.Accepted, summary.Refused), Is.EqualTo((2, 3)));
    }

    #endregion

    #region Stream Tests

    [Test]
    public void ReaderTakesAStreamThatGivesItsBytesOneAtATimeTest()
    {
        var bytes = BYTE_ORDER_MARK.Concat(Encoding.UTF8.GetBytes($"{FIRST}\r\n\r\n{SECOND}")).ToArray();
        using var stream = new SmtTricklingStream(bytes);
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var rows = new List<SmtProblemSetRow>();

        while (reader.TryRead(out var row))
            rows.Add(row);

        Assert.That(rows.Select(row => (row.Line, row.Data?.Id)), Is.EqualTo(new[] { (1, "first"), (3, "second") }));
        Assert.That(reader.Summary.BytesRead, Is.EqualTo(bytes.Length));
    }

    [Test]
    public void ReaderLeavesTheStreamToItsOwnerTest()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(FIRST));
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());

        while (reader.TryRead(out _))
        {
        }

        Assert.That(stream.CanRead, Is.True, "the reader does not close what it does not own");
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [Test]
    public void ErrorOfTheStreamIsTheCallersTest()
    {
        using var stream = new SmtFailingStream(Encoding.UTF8.GetBytes(FIRST + "\n" + SECOND + "\n"), failAt: FIRST.Length + 10);
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());

        Assert.Throws<IOException>(() =>
        {
            while (reader.TryRead(out _))
            {
            }
        });
    }

    [Test]
    public void SummaryCountsTheBytesReadRowByRowTest()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{FIRST}\n\n{SECOND}\r\n{BROKEN}"));
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var read = new List<long>();

        while (reader.TryRead(out _))
            read.Add(reader.Summary.BytesRead);

        Assert.That(read, Is.EqualTo(new long[] { FIRST.Length + 1, FIRST.Length + 2 + SECOND.Length + 2, stream.Length }));
        Assert.That(reader.Summary.BytesRead, Is.EqualTo(stream.Length));
    }

    #endregion

    #region Argument Tests

    [Test]
    public void ReaderRefusesNoStreamAndNoOptionsTest()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new SmtProblemSetReader(null!, new SmtOptionsData()));
        Assert.Throws<ArgumentNullException>(() => _ = new SmtProblemSetReader(new MemoryStream(), null!));
    }

    [Test]
    public void ReaderRefusesOptionsThatAreOutOfRangeTest()
    {
        // Options are checked before a set is read with them: no row is given a number of
        // models no run may have.
        var refused = Assert.Throws<ArgumentException>(() => _ = new SmtProblemSetReader(new MemoryStream(), new SmtOptionsData { MaxModels = 5_000 }))!;

        Assert.That(refused.Message, Does.StartWith("'max_models' must be between 1 and 1000"));
        Assert.That(refused.ParamName, Is.EqualTo("options"));
    }

    #endregion
}
