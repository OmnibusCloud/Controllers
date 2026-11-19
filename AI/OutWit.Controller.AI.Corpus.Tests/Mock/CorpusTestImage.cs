using System.Buffers.Binary;
using System.Text;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The image of a result written by hand, member by member, the way the wire has it: the tests'
/// own statement of the layout the server's walk follows. It is held to the serializer's bytes
/// (a result written here is the result the serializer writes), and it remembers where every
/// head of the image is, so a test can bend exactly one of them into what a dishonest node
/// would send.
/// </summary>
internal sealed class CorpusTestImage
{
    #region Constants

    /// <summary>The head of a null string and of a null list.</summary>
    public const int NULL_COLLECTION = -1;

    /// <summary>The head of a null object.</summary>
    public const byte NULL_OBJECT = 255;

    #endregion

    #region Fields

    private readonly MemoryStream m_bytes = new();
    private readonly List<CorpusTestImageHead> m_heads = [];
    private readonly bool m_utf16;

    #endregion

    #region Constructors

    /// <summary>Starts an empty image.</summary>
    /// <param name="utf16">Whether strings are written in the wire's UTF-16 form (the serializer's default is UTF-8).</param>
    public CorpusTestImage(bool utf16 = false)
    {
        m_utf16 = utf16;
    }

    #endregion

    #region Functions

    /// <summary>The image of a result.</summary>
    /// <param name="result">The result.</param>
    /// <param name="utf16">Whether strings are written in the wire's UTF-16 form.</param>
    /// <returns>The image with the places of its heads.</returns>
    public static CorpusTestImage Of(CorpusResultData result, bool utf16 = false)
    {
        var image = new CorpusTestImage(utf16);

        image.Begin("result", 17);
        image.Number(result.TaskIndex);
        image.Number((int)result.Verdict);
        image.Id(result.TextBlobId);
        image.Id(result.SignaturesBlobId);
        image.Text("content_sha256", result.ContentSha256);
        image.Text("error", result.Error);
        image.Stats(result.Stats);
        image.Id(result.NodeId);
        image.Text("node_name", result.NodeName);
        image.Text("controller_version", result.ControllerVersion);
        image.Text("source_url", result.SourceUrl);
        image.Text("runtime_target", result.RuntimeTarget);
        image.Number(result.Degree);
        image.Number((int)result.Output);

        if (result.MinHash == null)
            image.Null("minhash");
        else
        {
            image.Begin("minhash", 3);
            image.Number(result.MinHash.Hashes);
            image.Number(result.MinHash.ShingleWords);
            image.Number(result.MinHash.Bands);
        }

        image.Text("signatures_sha256", result.SignaturesSha256);
        image.Text("ticket", result.Ticket);

        return image;
    }

    /// <summary>The image's bytes.</summary>
    /// <returns>A copy.</returns>
    public byte[] ToArray()
    {
        return m_bytes.ToArray();
    }

    /// <summary>The image with one head replaced.</summary>
    /// <param name="head">The head, one of <see cref="Heads"/>.</param>
    /// <param name="value">What stands in its place: its low byte for the head of an object.</param>
    /// <returns>The bent image.</returns>
    public byte[] With(CorpusTestImageHead head, int value)
    {
        var bytes = ToArray();
        if (head.Kind == CorpusTestImageHeadKind.Object)
            bytes[head.Offset] = (byte)value;
        else
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(head.Offset), value);

        return bytes;
    }

    #endregion

    #region Tools

    private void Stats(CorpusShardStatsData? stats)
    {
        if (stats == null)
        {
            Null("stats");
            return;
        }

        Begin("stats", 12);
        Number(stats.RecordsIn);
        Number(stats.DocumentsExtracted);
        Number(stats.DocumentsOut);
        Counts("drops", stats.Drops);
        Counts("languages", stats.Languages);
        Wide(stats.BytesFetched);
        Wide(stats.BytesProduced);
        Number(stats.FetchMs);
        Number(stats.ProcessMs);
        Number(stats.WaitMs);
        Counts("unsupported_languages", stats.UnsupportedLanguages);

        if (stats.Pdf == null)
        {
            Null("pdf");
            return;
        }

        Begin("pdf", 8);
        Number(stats.Pdf.In);
        Number(stats.Pdf.Truncated);
        Number(stats.Pdf.Extracted);
        Number(stats.Pdf.Pages);
        Number(stats.Pdf.Documents);
        Number(stats.Pdf.PagesCut);
        Number(stats.Pdf.RunningLines);
        Wide(stats.Pdf.Fuel);
    }

    private void Counts(string name, List<CorpusCountData>? counts)
    {
        m_heads.Add(new CorpusTestImageHead(name, CorpusTestImageHeadKind.List, (int)m_bytes.Length));
        Number(counts?.Count ?? NULL_COLLECTION);

        for (var index = 0; index < (counts?.Count ?? 0); index++)
        {
            var count = counts![index];
            if (count == null)
            {
                Null($"{name}[{index}]");
                continue;
            }

            Begin($"{name}[{index}]", 2);
            Text($"{name}[{index}].name", count.Name);
            Number(count.Count);
        }
    }

    private void Begin(string name, byte members)
    {
        m_heads.Add(new CorpusTestImageHead(name, CorpusTestImageHeadKind.Object, (int)m_bytes.Length));
        m_bytes.WriteByte(members);
    }

    private void Null(string name)
    {
        Begin(name, NULL_OBJECT);
    }

    private void Text(string name, string? value)
    {
        m_heads.Add(new CorpusTestImageHead(name, CorpusTestImageHeadKind.Text, (int)m_bytes.Length));

        if (value == null)
        {
            Number(NULL_COLLECTION);
            return;
        }

        if (value.Length == 0)
        {
            Number(0);
            return;
        }

        if (m_utf16)
        {
            Number(value.Length);
            foreach (var unit in value)
            {
                m_bytes.WriteByte((byte)unit);
                m_bytes.WriteByte((byte)(unit >> 8));
            }

            return;
        }

        // UTF-8: the complement of the byte count, the character count, the bytes.
        var utf8 = Encoding.UTF8.GetBytes(value);
        Number(~utf8.Length);
        m_heads.Add(new CorpusTestImageHead(name + ".chars", CorpusTestImageHeadKind.Chars, (int)m_bytes.Length));
        Number(value.Length);
        m_bytes.Write(utf8);
    }

    private void Number(int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        m_bytes.Write(bytes);
    }

    private void Wide(long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        m_bytes.Write(bytes);
    }

    private void Id(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        m_bytes.Write(bytes);
    }

    #endregion

    #region Properties

    /// <summary>Every head of the image, in the order written: where a length, a count or a member count stands.</summary>
    public IReadOnlyList<CorpusTestImageHead> Heads => m_heads;

    #endregion
}
