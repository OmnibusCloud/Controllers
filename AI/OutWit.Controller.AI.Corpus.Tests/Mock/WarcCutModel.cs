using System.Globalization;
using System.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// What a reader has to make of a WARC stream that stops at a byte, worked out from the
/// stream's framing by a few lines of the tests' own and not by the reader under test: where
/// each record starts, where its headers end (the first blank line) and where its payload ends
/// (the <c>Content-Length</c> of its headers), with two blank lines after it.
/// <para>
/// A stream cut at a position holds, whole, the records whose payload ends at or before it. It
/// is truncated when the cut is inside a record (its headers or its payload) or between the
/// carriage return and the line feed of a blank line after one; a cut at the end of a payload,
/// after a whole blank line or at the start of a record leaves nothing unfinished.
/// </para>
/// </summary>
public sealed class WarcCutModel
{
    #region Constants

    private const string LENGTH_HEADER = "Content-Length: ";
    private const int BLANK_LINES_BYTES = 4;

    #endregion

    #region Fields

    private readonly byte[] m_stream;
    private readonly int[] m_starts;
    private readonly int[] m_headerEnds;
    private readonly int[] m_payloadEnds;

    #endregion

    #region Constructors

    private WarcCutModel(byte[] stream, int[] starts, int[] headerEnds, int[] payloadEnds)
    {
        m_stream = stream;
        m_starts = starts;
        m_headerEnds = headerEnds;
        m_payloadEnds = payloadEnds;
    }

    #endregion

    #region Functions

    /// <summary>The framing of a whole stream of records, unpacked.</summary>
    /// <param name="stream">The stream: records one after another, each with two blank lines after its payload.</param>
    /// <returns>The model.</returns>
    /// <exception cref="FormatException">The stream is not whole records: the model is for a stream that is.</exception>
    public static WarcCutModel Of(byte[] stream)
    {
        var starts = new List<int>();
        var headerEnds = new List<int>();
        var payloadEnds = new List<int>();

        var position = 0;
        while (position < stream.Length)
        {
            var headers = stream.AsSpan(position).IndexOf("\r\n\r\n"u8);
            if (headers < 0)
                throw new FormatException($"the record at {position} has no blank line after its headers");

            var headerEnd = position + headers + BLANK_LINES_BYTES;
            var payloadEnd = headerEnd + PayloadBytes(Encoding.Latin1.GetString(stream, position, headers), position);
            if (payloadEnd + BLANK_LINES_BYTES > stream.Length || !stream.AsSpan(payloadEnd, BLANK_LINES_BYTES).SequenceEqual("\r\n\r\n"u8))
                throw new FormatException($"the record at {position} has no two blank lines after its payload");

            starts.Add(position);
            headerEnds.Add(headerEnd);
            payloadEnds.Add(payloadEnd);
            position = payloadEnd + BLANK_LINES_BYTES;
        }

        return new WarcCutModel(stream, [.. starts], [.. headerEnds], [.. payloadEnds]);
    }

    /// <summary>What the stream holds when it stops at a position.</summary>
    /// <param name="position">How many bytes of the stream are there.</param>
    /// <returns>How many records are whole, and whether something after them was begun and not finished.</returns>
    public (int Whole, bool Truncated) At(int position)
    {
        var found = Array.BinarySearch(m_payloadEnds, position);
        var whole = found >= 0 ? found + 1 : ~found;

        if (whole < Count && position > m_starts[whole])
            return (whole, true);

        if (whole == 0)
            return (0, false);

        // Between two records: so many bytes of the two blank lines are there.
        var past = position - m_payloadEnds[whole - 1];
        return (whole, past is 1 or 3);
    }

    /// <summary>Holds what a reader returned to the first records of the stream.</summary>
    /// <param name="records">The records a reader returned.</param>
    /// <param name="whole">How many it had to return.</param>
    /// <returns>What is wrong, or <c>null</c> when the records are exactly the first of the stream.</returns>
    public string? Mismatch(IReadOnlyList<WarcRecord> records, int whole)
    {
        if (records.Count != whole)
            return $"{records.Count} records were returned, {whole} are whole";

        for (var index = 0; index < whole; index++)
        {
            var lines = Encoding.Latin1.GetString(m_stream, m_starts[index], m_headerEnds[index] - BLANK_LINES_BYTES - m_starts[index]).Split("\r\n");
            var record = records[index];

            if (record.Version != lines[0] || record.Headers.Count != lines.Length - 1)
                return $"record {index} came with other headers than the stream's";

            if (!record.Payload.Span.SequenceEqual(m_stream.AsSpan(m_headerEnds[index], m_payloadEnds[index] - m_headerEnds[index])))
                return $"record {index} came with another payload than the stream's";
        }

        return null;
    }

    /// <summary>Where a record's payload starts: the byte after the blank line of its headers.</summary>
    /// <param name="index">The record.</param>
    /// <returns>The position in the stream.</returns>
    public int HeaderEnd(int index)
    {
        return m_headerEnds[index];
    }

    private static int PayloadBytes(string headers, int at)
    {
        var lengths = headers.Split("\r\n").Where(line => line.StartsWith(LENGTH_HEADER, StringComparison.Ordinal)).ToList();
        if (lengths.Count != 1)
            throw new FormatException($"the record at {at} has {lengths.Count} Content-Length lines");

        return int.Parse(lengths[0].AsSpan(LENGTH_HEADER.Length), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Properties

    /// <summary>How many records the stream holds.</summary>
    public int Count => m_starts.Length;

    /// <summary>How many bytes the stream has.</summary>
    public int Length => m_stream.Length;

    #endregion
}
