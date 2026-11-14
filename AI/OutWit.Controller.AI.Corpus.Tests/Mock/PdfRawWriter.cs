using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Writes a PDF object by object, byte for byte as told, including what a sane writer would
/// refuse: a cross-reference table with wrong offsets or none, a trailer that lies, a
/// cross-reference stream whose size is absurd. Object numbers are assigned in order from 1.
/// </summary>
public sealed class PdfRawWriter
{
    #region Constants

    private static readonly Encoding LATIN1 = Encoding.Latin1;

    #endregion

    #region Fields

    private readonly List<byte[]?> m_objects = [];

    #endregion

    #region Functions

    /// <summary>Reserves an object number to be set later.</summary>
    /// <returns>The object number.</returns>
    public int Reserve()
    {
        m_objects.Add(null);
        return m_objects.Count;
    }

    /// <summary>Adds an object.</summary>
    /// <param name="body">The object's body, between "obj" and "endobj".</param>
    /// <returns>The object number.</returns>
    public int Add(string body)
    {
        m_objects.Add(LATIN1.GetBytes(body));
        return m_objects.Count;
    }

    /// <summary>Sets a reserved object.</summary>
    /// <param name="number">The object number.</param>
    /// <param name="body">The object's body.</param>
    public void Set(int number, string body)
    {
        m_objects[number - 1] = LATIN1.GetBytes(body);
    }

    /// <summary>Adds a stream object.</summary>
    /// <param name="dictionary">The dictionary entries without /Length.</param>
    /// <param name="data">The stream bytes as stored.</param>
    /// <returns>The object number.</returns>
    public int AddStream(string dictionary, byte[] data)
    {
        m_objects.Add(Stream(dictionary, data));
        return m_objects.Count;
    }

    /// <summary>Sets a reserved object to a stream.</summary>
    /// <param name="number">The object number.</param>
    /// <param name="dictionary">The dictionary entries without /Length.</param>
    /// <param name="data">The stream bytes as stored.</param>
    public void SetStream(int number, string dictionary, byte[] data)
    {
        m_objects[number - 1] = Stream(dictionary, data);
    }

    /// <summary>Writes the file with a classic cross-reference table.</summary>
    /// <param name="root">The catalog's object number.</param>
    /// <param name="offsetShift">Added to every offset in the table (0 = correct offsets).</param>
    /// <param name="writeXref">False writes no table and no trailer, only the objects and %%EOF.</param>
    /// <param name="startXref">A value for startxref other than the table's offset, or null.</param>
    /// <param name="trailerExtra">Extra trailer entries.</param>
    /// <returns>The file.</returns>
    public byte[] Build(int root, long offsetShift = 0, bool writeXref = true, long? startXref = null, string trailerExtra = "")
    {
        using var output = new MemoryStream();
        var offsets = WriteObjects(output);

        if (!writeXref)
        {
            Write(output, "%%EOF\n");
            return output.ToArray();
        }

        var xref = output.Position;
        var table = new StringBuilder();
        table.Append($"xref\n0 {m_objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            table.Append($"{offset + offsetShift:D10} 00000 n \n");

        table.Append($"trailer\n<< /Size {m_objects.Count + 1} /Root {root} 0 R {trailerExtra}>>\nstartxref\n{startXref ?? xref}\n%%EOF\n");
        Write(output, table.ToString());
        return output.ToArray();
    }

    /// <summary>
    /// Writes the file with a cross-reference stream (PDF 1.5) that indexes the objects written
    /// here plus, as compressed entries (type 2), the objects of an object stream.
    /// </summary>
    /// <param name="root">The catalog's object number.</param>
    /// <param name="compressed">Objects inside object streams: (object number, object stream number, index).</param>
    /// <param name="declaredSize">The /Size the stream declares, or null for the true size.</param>
    /// <returns>The file.</returns>
    public byte[] BuildWithXrefStream(int root, IReadOnlyList<(int Number, int Stream, int Index)> compressed, long? declaredSize = null)
    {
        using var output = new MemoryStream();
        var offsets = WriteObjects(output);

        var xrefNumber = m_objects.Count + 1;
        var size = xrefNumber + 1;
        var rows = new byte[size * 7];
        rows[0] = 0;
        WriteBigEndian(rows, 1, 0);
        WriteBigEndian16(rows, 5, 65535);

        for (var number = 1; number < size; number++)
        {
            var row = number * 7;
            var packed = compressed.FirstOrDefault(entry => entry.Number == number);
            if (number == xrefNumber)
            {
                rows[row] = 1;
                WriteBigEndian(rows, row + 1, output.Position);
            }
            else if (packed.Number == number)
            {
                rows[row] = 2;
                WriteBigEndian(rows, row + 1, packed.Stream);
                WriteBigEndian16(rows, row + 5, packed.Index);
            }
            else if (m_objects[number - 1] != null)
            {
                rows[row] = 1;
                WriteBigEndian(rows, row + 1, offsets[number - 1]);
            }
        }

        var xref = output.Position;
        var declared = declaredSize ?? size;
        Write(output, $"{xrefNumber} 0 obj\n<< /Type /XRef /Size {declared} /Index [0 {declared}] /W [1 4 2] /Root {root} 0 R /Length {rows.Length} >>\nstream\n");
        output.Write(rows);
        Write(output, $"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private List<long> WriteObjects(MemoryStream output)
    {
        Write(output, "%PDF-1.7\n%âãÏÓ\n");
        var offsets = new List<long>();
        for (var index = 0; index < m_objects.Count; index++)
        {
            offsets.Add(output.Position);
            var body = m_objects[index];
            if (body == null)
                continue;

            Write(output, $"{index + 1} 0 obj\n");
            output.Write(body);
            Write(output, "\nendobj\n");
        }

        return offsets;
    }

    private static byte[] Stream(string dictionary, byte[] data)
    {
        using var output = new MemoryStream();
        Write(output, $"<< {dictionary} /Length {data.Length} >>\nstream\n");
        output.Write(data);
        Write(output, "\nendstream");
        return output.ToArray();
    }

    private static void Write(Stream output, string text)
    {
        output.Write(LATIN1.GetBytes(text));
    }

    private static void WriteBigEndian(byte[] target, int at, long value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static void WriteBigEndian16(byte[] target, int at, int value)
    {
        target[at] = (byte)(value >> 8);
        target[at + 1] = (byte)value;
    }

    #endregion
}
