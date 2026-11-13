using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Salvage;

/// <summary>
/// Makes a truncated PDF readable: scans the bytes that arrived for every complete object
/// ("N G obj" ... "endobj"), indexes the objects inside complete Flate object streams, gathers
/// every complete page object (in file order) under a new page tree with a new catalog, and
/// appends a cross-reference stream that indexes all of it. The pages that were cut off are
/// simply not there; the ones that arrived become a document an ordinary parser opens.
/// </summary>
public static partial class PdfSalvage
{
    #region Constants

    private const int OBJECT_STREAM_CAP = 64 << 20;
    private const int DICTIONARY_PEEK = 4000;

    #endregion

    #region Functions

    /// <summary>Salvages a truncated document.</summary>
    /// <param name="data">The bytes that arrived.</param>
    /// <returns>The repaired document, or null when no page object is complete.</returns>
    public static byte[]? Repair(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        var found = new Dictionary<int, (int Offset, int Generation, int Body, int End)>();

        foreach (Match match in ObjectHeader().Matches(text))
        {
            if (!int.TryParse(match.Groups[1].Value, out var number) || !int.TryParse(match.Groups[2].Value, out var generation))
                continue;

            var end = text.IndexOf("endobj", match.Index + match.Length, StringComparison.Ordinal);
            var next = ObjectHeader().Match(text, match.Index + match.Length);
            if (end < 0 || (next.Success && next.Index < end))
                continue;

            found[number] = (match.Index, generation, match.Index + match.Length, end);
        }

        var compressed = new Dictionary<int, (int Stream, int Index)>();
        foreach (var (number, place) in found.ToArray())
            IndexObjectStream(data, text, number, place.Body, place.End, compressed);

        var pages = found
            .OrderBy(entry => entry.Value.Offset)
            .Where(entry => PageType().IsMatch(Peek(text, entry.Value.Body, entry.Value.End)))
            .Select(entry => entry.Key)
            .ToList();

        if (pages.Count == 0)
            return null;

        var top = found.Keys.Concat(compressed.Keys).DefaultIfEmpty(0).Max() + 1;
        using var output = new MemoryStream();
        output.Write(data);
        if (data.Length == 0 || data[^1] != (byte)'\n')
            output.WriteByte((byte)'\n');

        var treeOffset = output.Position;
        Write(output, $"{top} 0 obj\n<< /Type /Pages /Kids [{string.Join(' ', pages.Select(page => $"{page} 0 R"))}] /Count {pages.Count} >>\nendobj\n");
        var catalogOffset = output.Position;
        Write(output, $"{top + 1} 0 obj\n<< /Type /Catalog /Pages {top} 0 R >>\nendobj\n");

        var xrefNumber = top + 2;
        var size = xrefNumber + 1;
        var xrefOffset = output.Position;
        var rows = new byte[size * 7];
        for (var number = 0; number < size; number++)
        {
            var row = number * 7;
            if (number == top)
                Row(rows, row, 1, treeOffset, 0);
            else if (number == top + 1)
                Row(rows, row, 1, catalogOffset, 0);
            else if (number == xrefNumber)
                Row(rows, row, 1, xrefOffset, 0);
            else if (found.TryGetValue(number, out var place))
                Row(rows, row, 1, place.Offset, place.Generation);
            else if (compressed.TryGetValue(number, out var packed))
                Row(rows, row, 2, packed.Stream, packed.Index);
            else
                Row(rows, row, 0, 0, 65535);
        }

        Write(output, $"{xrefNumber} 0 obj\n<< /Type /XRef /Size {size} /W [1 4 2] /Root {top + 1} 0 R /Length {rows.Length} >>\nstream\n");
        output.Write(rows);
        Write(output, $"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static void IndexObjectStream(byte[] data, string text, int number, int body, int end, Dictionary<int, (int Stream, int Index)> compressed)
    {
        var head = Peek(text, body, end);
        if (!head.Contains("/ObjStm", StringComparison.Ordinal))
            return;

        var first = FirstEntry().Match(head);
        var count = CountEntry().Match(head);
        var start = text.IndexOf("stream", body, end - body, StringComparison.Ordinal);
        var stop = text.LastIndexOf("endstream", end, end - body, StringComparison.Ordinal);
        if (!first.Success || !count.Success || start < 0 || stop < 0 || stop <= start)
            return;

        start += "stream".Length;
        while (start < stop && (data[start] == '\r' || data[start] == '\n'))
            start++;

        byte[] decoded;
        try
        {
            using var source = new MemoryStream(data, start, stop - start, writable: false);
            using var inflater = new ZLibStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream();
            var buffer = new byte[1 << 16];
            int read;
            while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0 && target.Length < OBJECT_STREAM_CAP)
                target.Write(buffer, 0, read);
            decoded = target.ToArray();
        }
        catch (InvalidDataException)
        {
            return;
        }

        var headerLength = Math.Min(int.Parse(first.Groups[1].Value), decoded.Length);
        var numbers = Encoding.Latin1.GetString(decoded, 0, headerLength).Split((char[])[' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var objects = Math.Min(numbers.Length / 2, int.Parse(count.Groups[1].Value));
        for (var index = 0; index < objects; index++)
        {
            if (int.TryParse(numbers[index * 2], out var inner))
                compressed.TryAdd(inner, (number, index));
        }
    }

    private static string Peek(string text, int body, int end)
    {
        return text.Substring(body, Math.Min(end - body, DICTIONARY_PEEK));
    }

    private static void Row(byte[] rows, int at, byte type, long field2, int field3)
    {
        rows[at] = type;
        rows[at + 1] = (byte)(field2 >> 24);
        rows[at + 2] = (byte)(field2 >> 16);
        rows[at + 3] = (byte)(field2 >> 8);
        rows[at + 4] = (byte)field2;
        rows[at + 5] = (byte)(field3 >> 8);
        rows[at + 6] = (byte)field3;
    }

    private static void Write(Stream output, string text)
    {
        output.Write(Encoding.Latin1.GetBytes(text));
    }

    [GeneratedRegex(@"(?<![0-9])(\d{1,10})\s+(\d{1,5})\s+obj\b", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectHeader();

    [GeneratedRegex(@"/Type\s*/Page(?![A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex PageType();

    [GeneratedRegex(@"/First\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex FirstEntry();

    [GeneratedRegex(@"/N\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex CountEntry();

    #endregion
}
