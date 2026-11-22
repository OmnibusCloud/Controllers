using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Tests.Problems;

/// <summary>
/// How much a set may hold, and that a reader holds one line of it and no more: the longest
/// line, a line that is too long, and a set of more rows than a set may have, read from a stream
/// that is made while it is read.
/// </summary>
[TestFixture]
public class SmtProblemSetLimitTests
{
    #region Constants

    private const string NEXT = """{"id":"next","smt2":"(assert true)"}""";
    private const int MEBIBYTE = 1024 * 1024;

    #endregion

    #region Line Tests

    [Test]
    public void LimitsAreTheNumbersOfTheContractTest()
    {
        Assert.That(SmtProblemSetLimits.MAX_LINE_BYTES, Is.EqualTo(16 * MEBIBYTE));
        Assert.That(SmtProblemSetLimits.MAX_ROWS, Is.EqualTo(1_000_000));
        Assert.That(SmtProblemSetLimits.MAX_ID_CHARACTERS, Is.EqualTo(64));
        Assert.That(SmtProblemSetLimits.MAX_VALUES, Is.EqualTo(256));
        Assert.That(SmtProblemSetLimits.MAX_PROJECT, Is.EqualTo(256));
        Assert.That(SmtProblemSetLimits.MAX_TAGS_CHARACTERS, Is.EqualTo(256));
        Assert.That(SmtProblemSetLimits.MAX_REFUSALS_KEPT, Is.EqualTo(1_000));
        Assert.That(SmtProblemSetLimits.MAX_LICENSES, Is.EqualTo(64));
        Assert.That(SmtProblemSetLimits.MAX_LOGICS, Is.EqualTo(64));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    public void LineOfSixteenMebibytesIsReadAndOneByteMoreIsRefusedTest(string lineEnd)
    {
        // The line end is no part of the line: a file is held to the same limit whichever way it
        // ends its lines.
        var longest = Padded(SmtProblemSetLimits.MAX_LINE_BYTES);
        var tooLong = Padded(SmtProblemSetLimits.MAX_LINE_BYTES + 1);

        var rows = Rows(Pieces(longest, lineEnd, tooLong, lineEnd, NEXT, lineEnd, tooLong), out var summary);

        Assert.That(rows.Select(row => (row.Line, row.Index, row.IsAccepted)), Is.EqualTo(new[] { (1, 0, true), (2, 1, false), (3, 2, true), (4, 3, false) }));
        Assert.That(rows[0].Data!.Id, Is.EqualTo("long"));
        Assert.That(rows[2].Data!.Id, Is.EqualTo("next"), "the row after a line that is too long is read");

        foreach (var refused in new[] { rows[1], rows[3] })
        {
            Assert.That(refused.Refusal!.Code, Is.EqualTo(SmtRefusalCodes.ROW_TOO_LONG));
            Assert.That(refused.Refusal.Reason, Is.EqualTo("the line is longer than the 16777216 bytes a line may have"));
            Assert.That((refused.Refusal.Row, refused.Refusal.Line, refused.Refusal.File), Is.EqualTo((refused.Line, 0, "")), "nothing of such a line is read, its id included");
        }

        Assert.That(summary.BytesRead, Is.EqualTo(2L * SmtProblemSetLimits.MAX_LINE_BYTES + 2 + SmtProblemSetLimits.MAX_LINE_BYTES + NEXT.Length + 3 * lineEnd.Length));
    }

    [Test]
    public void LineThatIsTooLongIsPassedOverWithoutBeingHeldTest()
    {
        // 256 MiB of one line, made while it is read: the reader keeps what a line may have at
        // most, and nothing of the rest.
        const int pieces = 256;
        var piece = Encoding.ASCII.GetBytes(new string('x', MEBIBYTE));
        IEnumerable<byte[]> Line()
        {
            yield return Encoding.ASCII.GetBytes("{\"id\":\"huge\",\"smt2\":\"");
            for (var index = 0; index < pieces; index++)
                yield return piece;

            yield return Encoding.ASCII.GetBytes("\"}\n" + NEXT + "\n");
        }

        using var stream = new SmtGeneratedStream(Line());
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.That(reader.TryRead(out var huge), Is.True);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(reader.TryRead(out var next), Is.True);
        Assert.That(reader.TryRead(out _), Is.False);

        Assert.That(huge!.Refusal!.Code, Is.EqualTo(SmtRefusalCodes.ROW_TOO_LONG));
        Assert.That(next!.Data!.Id, Is.EqualTo("next"));
        Assert.That(stream.BytesGiven, Is.GreaterThan(pieces * (long)MEBIBYTE));
        Assert.That(allocated, Is.LessThan(4L * SmtProblemSetLimits.MAX_LINE_BYTES), "a line of 256 MiB costs no more than a few lines of the longest a set may have");
    }

    [Test]
    public void LinesOfEveryLengthAroundTheReadersBuffersAreReadWholeTest()
    {
        // The reader reads in pieces of its own size: a line that ends just before, at and just
        // after the end of a piece is the same line.
        var random = new Random(20261123);
        var lengths = new List<int>();
        foreach (var around in new[] { 16 * 1024, 128 * 1024, 256 * 1024 })
            lengths.AddRange(Enumerable.Range(-3, 7).Select(delta => around + delta));

        lengths.AddRange(Enumerable.Range(0, 40).Select(_ => random.Next(40, 300_000)));

        var lines = lengths.Select((length, index) => Padded(length, $"l{index}")).ToList();
        var set = new List<string>();
        foreach (var line in lines)
        {
            set.Add(line);
            set.Add(random.Next(3) == 0 ? "\r\n" : "\n");
        }

        var rows = Rows(Pieces(set.ToArray()), out var summary);

        Assert.That(rows.Select(row => row.Data?.Id), Is.EqualTo(lengths.Select((_, index) => $"l{index}")));
        Assert.That(summary.BytesRead, Is.EqualTo(set.Sum(part => (long)part.Length)));
    }

    #endregion

    #region Row Count Tests

    [Test]
    public void RowAfterTheMillionthIsRefusedOnceAndReadingEndsTest()
    {
        // 1,000,005 rows made while they are read; the set is never in memory, and what the
        // reader remembers of a million rows is their ids, a few bytes each.
        const int extra = 5;
        IEnumerable<byte[]> Lines()
        {
            for (var index = 0; index < SmtProblemSetLimits.MAX_ROWS + extra; index++)
                yield return Encoding.ASCII.GetBytes($"{{\"id\":\"r{index}\",\"smt2\":\"(assert true)\"}}\n");
        }

        using var stream = new SmtGeneratedStream(Lines());
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var before = GC.GetTotalMemory(forceFullCollection: true);

        SmtProblemSetRow? last = null;
        var rows = 0;
        var refused = 0;
        while (reader.TryRead(out var row))
        {
            rows++;
            refused += row.IsAccepted ? 0 : 1;
            last = row;
        }

        var held = GC.GetTotalMemory(forceFullCollection: true) - before;

        Assert.That(rows, Is.EqualTo(SmtProblemSetLimits.MAX_ROWS + 1));
        Assert.That(refused, Is.EqualTo(1));
        Assert.That(last!.Refusal!.Code, Is.EqualTo(SmtRefusalCodes.TOO_MANY_PROBLEMS));
        Assert.That(last.Refusal.Reason, Is.EqualTo("a problem set has at most 1000000 rows; this row and what follows it were not read"));
        Assert.That((last.Line, last.Index, last.Refusal.Row, last.Refusal.Line, last.Refusal.File), Is.EqualTo((1_000_001, 1_000_000, 1_000_001, 0, "")));

        Assert.That(reader.TryRead(out _), Is.False, "said once");
        Assert.That(reader.TryRead(out _), Is.False);
        Assert.That((reader.Summary.Rows, reader.Summary.Accepted, reader.Summary.Refused), Is.EqualTo((1_000_001, 1_000_000, 1)));
        Assert.That(reader.Summary.RefusalCodes[SmtRefusalCodes.TOO_MANY_PROBLEMS], Is.EqualTo(1));
        Assert.That(reader.Summary.Refusals, Has.Count.EqualTo(1));

        Assert.That(held, Is.LessThan(160L * MEBIBYTE), "a million rows are remembered by a few bytes each");
        GC.KeepAlive(reader);
    }

    #endregion

    #region Tools

    /// <summary>A row of exactly so many bytes: blanks between two of its fields fill it.</summary>
    private static string Padded(int bytes, string id = "long")
    {
        var start = $"{{\"id\":\"{id}\",";
        const string end = "\"smt2\":\"(assert true)\"}";

        return start + new string(' ', bytes - start.Length - end.Length) + end;
    }

    private static IEnumerable<byte[]> Pieces(params string[] parts)
    {
        foreach (var part in parts)
            yield return Encoding.ASCII.GetBytes(part);
    }

    private static List<SmtProblemSetRow> Rows(IEnumerable<byte[]> pieces, out SmtProblemSetSummary summary)
    {
        using var stream = new SmtGeneratedStream(pieces);
        var reader = new SmtProblemSetReader(stream, new SmtOptionsData());
        var rows = new List<SmtProblemSetRow>();

        while (reader.TryRead(out var row))
        {
            SmtProblemSets.Hold(row);
            rows.Add(row);
        }

        summary = reader.Summary;
        Assert.That(summary.BytesRead, Is.EqualTo(stream.BytesGiven));
        return rows;
    }

    #endregion
}
