using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// A problem set has a bound in bytes, every byte counted as it arrives: its rows, their line
/// ends, blank lines and the byte order mark. The line that would take the set past it is refused
/// once (<c>set_too_large</c>), nothing of it or after it is read, and the stream is not read on to
/// its end. Lines are numbered without wrapping, however many blank lines a set holds. The sets
/// are made while they are read, gigabytes of them included.
/// </summary>
[TestFixture]
public sealed class SmtProblemSetBytesTests
{
    #region Constants

    private const string ROW = """{"id":"p","smt2":"(assert true)"}""";
    private const string NEXT = """{"id":"next","smt2":"(assert true)"}""";
    private const string LAST = """{"id":"last","smt2":"(assert true)"}""";
    private const int MEBIBYTE = 1024 * 1024;

    // What a reader may have read of a stream past the set's bound: one buffer of the stream.
    private const int READ_BUFFER_BYTES = 128 * 1024;

    #endregion

    #region Bound Tests

    [Test]
    public void BoundIsTheNumberOfTheContractTest()
    {
        var schema = File.ReadAllText(Path.Combine(Root(), "AI", "OutWit.Controller.AI.Smt.Model", "docs", "smt-problems.schema.json"));

        Assert.That(SmtProblemSetLimits.MAX_SET_BYTES, Is.EqualTo(4L * 1024 * MEBIBYTE));
        Assert.That(schema, Does.Contain($"a set has at most {SmtProblemSetLimits.MAX_SET_BYTES} bytes (4 GiB)"), "the schema states the bound");
        Assert.That(schema, Does.Contain(SmtRefusalCodes.SET_TOO_LARGE), "and its code");
    }

    [Test]
    public void SetPastItsBoundIsRefusedOnceAndReadingEndsTest()
    {
        // Four gibibytes of blank lines, each of a mebibyte of spaces, and a row after them: the
        // row stands past the bound and is not read; the stream is not read to its end.
        var blank = Encoding.ASCII.GetBytes(new string(' ', MEBIBYTE - 1) + "\n");
        var lines = (int)(SmtProblemSetLimits.MAX_SET_BYTES / blank.Length);
        var row = Encoding.ASCII.GetBytes(NEXT + "\n");
        using var stream = new SmtGeneratedStream(Enumerable.Repeat(blank, lines).Append(row).Append(blank));

        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var rows = new List<SmtProblemSetRow>();
        while (reader.TryRead(out var read))
            rows.Add(read);

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1), "one refusal, and nothing after it");
            Assert.That(rows[0].IsAccepted, Is.False);
            Assert.That(rows[0].Refusal!.Code, Is.EqualTo(SmtRefusalCodes.SET_TOO_LARGE));
            Assert.That(rows[0].Refusal!.Reason, Is.EqualTo("a problem set has at most 4294967296 bytes; this line and what follows it were not read"));
            Assert.That((rows[0].Line, rows[0].Index, rows[0].Refusal!.Row), Is.EqualTo((lines + 1, 0, lines + 1)), "the line that passes the bound, the first row of the set");
            Assert.That(reader.Summary.BytesRead, Is.EqualTo((long)lines * blank.Length), "every byte up to the line that passes the bound");
            Assert.That(reader.Summary.RefusalCodes[SmtRefusalCodes.SET_TOO_LARGE], Is.EqualTo(1));
            Assert.That(stream.BytesGiven, Is.LessThanOrEqualTo(SmtProblemSetLimits.MAX_SET_BYTES + READ_BUFFER_BYTES), "the stream was not read on");
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SetOfItsBoundIsReadWholeAndOneByteMoreIsNotTest(int lineEndOfTheLast)
    {
        // Three rows, the last with or without its line end, under a bound of exactly their bytes.
        var lastEnd = lineEndOfTheLast switch { 0 => "", 1 => "\n", _ => "\r\n" };
        var text = $"{ROW}\n\n{NEXT}\r\n{LAST}{lastEnd}";
        var bound = Encoding.ASCII.GetByteCount(text);

        var whole = Read(text, bound, out var wholeSummary);
        var short1 = Read(text, bound - 1, out var shortSummary);

        Assert.Multiple(() =>
        {
            Assert.That(whole.Select(row => (row.Line, row.IsAccepted)), Is.EqualTo(new[] { (1, true), (3, true), (4, true) }));
            Assert.That(wholeSummary.BytesRead, Is.EqualTo(bound));
            Assert.That(short1.Select(row => (row.Line, row.IsAccepted, row.Refusal?.Code)), Is.EqualTo(new[] { (1, true, (string?)null), (3, true, null), (4, false, SmtRefusalCodes.SET_TOO_LARGE) }),
                "the last line passes the bound by one byte");
            Assert.That(shortSummary.BytesRead, Is.LessThanOrEqualTo(bound - 1));
        });
    }

    [Test]
    public void BlankLinesAndTheByteOrderMarkCountTowardsTheBoundTest()
    {
        // Line feeds and nothing else are no rows, and still bytes of the set.
        var feeds = new byte[MEBIBYTE];
        Array.Fill(feeds, (byte)'\n');
        var mark = new byte[] { 0xEF, 0xBB, 0xBF };
        using var stream = new SmtGeneratedStream(new[] { mark }.Concat(Enumerable.Repeat(feeds, 64)));

        var rows = new List<SmtProblemSetRow>();
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData(), 4L * MEBIBYTE);
        while (reader.TryRead(out var read))
            rows.Add(read);

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => (row.Index, row.Refusal?.Code)), Is.EqualTo(new[] { (0, (string?)SmtRefusalCodes.SET_TOO_LARGE) }));
            Assert.That(rows[0].Line, Is.EqualTo(4 * MEBIBYTE - 3 + 1), "the line feeds the bound takes, the mark's three bytes among them, and the next line");
            Assert.That(reader.Summary.BytesRead, Is.EqualTo(4L * MEBIBYTE));
            Assert.That(stream.BytesGiven, Is.LessThan(5L * MEBIBYTE), "sixty-four mebibytes of line feeds are not read to their end");
        });
    }

    [Test]
    public void LineThatPassesTheBoundIsNotReadTest()
    {
        var text = $"{ROW}\n{new string('x', 2048)}\n{NEXT}\n";

        var rows = Read(text, ROW.Length + 1 + 1024, out _);

        Assert.That(rows.Select(row => (row.Line, row.Refusal?.Code)), Is.EqualTo(new[] { (1, (string?)null), (2, SmtRefusalCodes.SET_TOO_LARGE) }));
    }

    #endregion

    #region Line Tests

    [Test]
    [Category("Load")]
    [CancelAfter(600_000)]
    public void LinesPastTheLargestNumberOfARowAreNotNumberedNegativeTest()
    {
        // Two gibibytes and more of line feeds, then a row: its line is past the largest number a
        // row's line can be, and is numbered as that number, never as one that wrapped.
        var feeds = new byte[64 * MEBIBYTE];
        Array.Fill(feeds, (byte)'\n');
        var pieces = (int)((int.MaxValue + 64L * MEBIBYTE) / feeds.Length);
        using var stream = new SmtGeneratedStream(Enumerable.Repeat(feeds, pieces).Append(Encoding.ASCII.GetBytes(NEXT)));

        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        Assert.That(reader.TryRead(out var row), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(row!.IsAccepted, Is.True, row.Refusal?.Reason);
            Assert.That(row.Line, Is.EqualTo(int.MaxValue));
            Assert.That(row.Index, Is.Zero);
            Assert.That(reader.TryRead(out _), Is.False);
        });
    }

    #endregion

    #region Tools

    private static string Root()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, "the solution root (OutWit.slnx) was not found above the test folder");
        return directory!.FullName;
    }

    private static List<SmtProblemSetRow> Read(string text, long maxSetBytes, out SmtProblemSetSummary summary)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData(), maxSetBytes);
        var rows = new List<SmtProblemSetRow>();
        while (reader.TryRead(out var row))
            rows.Add(row);

        summary = reader.Summary;
        return rows;
    }

    #endregion
}
