using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// PDFs written by this code alone, nothing third-party: the extractor's contract set (the port
/// of the module toolchain's <c>pdfs.py</c>: pages in tree order, an empty page, a hyphen at a
/// line end, control characters, RC4 encryption with and without a user password, no pages, not a
/// PDF, an empty file) and the documents the WARC fixture is made of — pages of Helvetica lines
/// with a running header and footer.
/// </summary>
public static class PdfSynth
{
    #region Constants

    /// <summary>The standard font every page uses (no font file: the extractor maps WinAnsi itself).</summary>
    public const string HELVETICA = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    /// <summary>The three pages of the three-page cases.</summary>
    public static readonly IReadOnlyList<string> THREE_PAGES =
    [
        "Alpha is the first page of three.",
        "Bravo is the second page, in the middle.",
        "Charlie is the third and last page."
    ];

    /// <summary>Four lines: a word broken at a line end (lower case on both sides), then a hyphen that belongs to a label.</summary>
    public static readonly IReadOnlyList<string> HYPHEN_LINES =
    [
        "The words on this line end in a hyphen-",
        "ated word and the sentence goes on.",
        "A label like COVID-",
        "19 keeps its hyphen at the line end."
    ];

    /// <summary>What the extractor makes of <see cref="HYPHEN_LINES"/>: the broken word rejoined, the label's hyphen kept.</summary>
    public const string HYPHEN_TEXT = "The words on this line end in a hyphenated word and the sentence goes on.\nA label like COVID-\n19 keeps its hyphen at the line end.";

    /// <summary>The text of the document encrypted with the empty user password.</summary>
    public const string EMPTY_PASSWORD_TEXT = "This page is encrypted with the empty user password.";

    private const string PAGE_BOX = "/MediaBox [0 0 612 792]";
    private const int PERMISSIONS = -3904;

    // ISO 32000-1, 7.6.3.3: the padding of a password to 32 bytes.
    private static readonly byte[] PASSWORD_PADDING = Convert.FromHexString("28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");
    private static readonly byte[] FILE_ID = Enumerable.Range(0x30, 16).Select(value => (byte)value).ToArray();

    private static readonly string[] WORDS =
    [
        "the", "river", "valley", "farmers", "planted", "wheat", "and", "barley", "every", "spring", "while", "their",
        "children", "learned", "to", "read", "old", "letters", "about", "weather", "markets", "roads", "bridges", "built",
        "by", "patient", "workers", "who", "carried", "stone", "from", "quarries", "near", "village", "school", "library",
        "kept", "records", "of", "harvests", "floods", "births", "journeys", "in", "a", "careful", "hand", "for", "many", "years"
    ];

    #endregion

    #region Contract

    /// <summary>The contract set, by file name, in the toolchain's order.</summary>
    /// <returns>Name and bytes of each case.</returns>
    public static IReadOnlyList<(string Name, byte[] Bytes)> Contract()
    {
        return
        [
            ("one-page.pdf", Document([["The only page of a one-page document."]])),
            ("three-pages.pdf", Document(THREE_PAGES.Select(text => (IReadOnlyList<string>)[text]).ToList())),
            ("three-pages-nested-tree.pdf", ThreePagesNested()),
            ("empty-page.pdf", Document([[THREE_PAGES[0]], [], [THREE_PAGES[2]]])),
            ("hyphenated-line.pdf", Hyphenated()),
            ("control-characters.pdf", ControlCharacters()),
            ("encrypted-empty-password.pdf", Encrypted("", EMPTY_PASSWORD_TEXT)),
            ("encrypted-user-password.pdf", Encrypted("a user password", "This page needs a password.")),
            ("no-pages.pdf", NoPages()),
            ("not-a-pdf.pdf", Encoding.Latin1.GetBytes("<!DOCTYPE html>\n<html><body><p>This is a web page, not a PDF.</p></body></html>\n")),
            ("empty-file.pdf", [])
        ];
    }

    /// <summary>A plain document: each page's lines one under the other in Helvetica 12 (a page without lines has an empty content stream).</summary>
    /// <param name="pages">The lines of each page.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Document(IReadOnlyList<IReadOnlyList<string>> pages)
    {
        return Pages(pages.Select(lines => lines.Count == 0 ? "" : Block(12, 72, 720, 16, lines)).ToList());
    }

    /// <summary>Three pages whose objects are written last page first under a two-level tree: the order is the tree's, not the file's.</summary>
    /// <returns>The PDF.</returns>
    public static byte[] ThreePagesNested()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var branch = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var numbers = new int[3];
        foreach (var index in new[] { 2, 1, 0 })
        {
            var content = writer.AddStream("", Latin1(Block(12, 72, 720, 16, [THREE_PAGES[index]])));
            numbers[index] = writer.Add(Page(index == 2 ? tree : branch, font, content));
        }

        writer.Set(branch, Invariant($"<< /Type /Pages /Parent {tree} 0 R /Kids [{numbers[0]} 0 R {numbers[1]} 0 R] /Count 2 >>"));
        writer.Set(catalog, Invariant($"<< /Type /Catalog /Pages {tree} 0 R >>"));
        writer.Set(tree, Invariant($"<< /Type /Pages /Kids [{branch} 0 R {numbers[2]} 0 R] /Count 3 >>"));
        return writer.Build(catalog);
    }

    /// <summary>One page of the four <see cref="HYPHEN_LINES"/>.</summary>
    /// <returns>The PDF.</returns>
    public static byte[] Hyphenated()
    {
        return Pages([Block(12, 72, 720, 16, HYPHEN_LINES)]);
    }

    /// <summary>One page whose strings hold a carriage return and a form feed (octal escapes).</summary>
    /// <returns>The PDF.</returns>
    public static byte[] ControlCharacters()
    {
        return Pages(["BT /F1 12 Tf 72 720 Td (carriage\\015return) Tj 0 -16 Td (form\\014feed) Tj ET"]);
    }

    /// <summary>A catalog and an empty page tree, nothing else.</summary>
    /// <returns>The PDF.</returns>
    public static byte[] NoPages()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        writer.Set(catalog, Invariant($"<< /Type /Catalog /Pages {tree} 0 R >>"));
        writer.Set(tree, "<< /Type /Pages /Kids [] /Count 0 >>");
        return writer.Build(catalog);
    }

    /// <summary>
    /// One page encrypted by the Standard security handler, revision 3, 128-bit RC4 (ISO 32000-1,
    /// 7.6.3): with the empty user password the extractor reads it, with another it refuses it.
    /// </summary>
    /// <param name="userPassword">The user password ("" = the empty one).</param>
    /// <param name="text">The page's one line.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Encrypted(string userPassword, string text)
    {
        var (ownerEntry, userEntry, fileKey) = StandardSecurity(Latin1(userPassword), Latin1("the owner's password"));
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = writer.Reserve();
        writer.SetStream(content, "", Rc4(ObjectKey(fileKey, content), Latin1(Block(12, 72, 720, 16, [text]))));
        var page = writer.Add(Page(tree, font, content));
        var encrypt = writer.Add(Invariant($"<< /Filter /Standard /V 2 /R 3 /Length 128 /O <{Convert.ToHexString(ownerEntry)}> /U <{Convert.ToHexString(userEntry)}> /P {PERMISSIONS} >>"));
        writer.Set(catalog, Invariant($"<< /Type /Catalog /Pages {tree} 0 R >>"));
        writer.Set(tree, Invariant($"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>"));
        var id = Convert.ToHexString(FILE_ID);
        return writer.Build(catalog, trailerExtra: $"/Encrypt {encrypt} 0 R /ID [<{id}> <{id}>] ");
    }

    #endregion

    #region Books

    /// <summary>
    /// A document of pages laid out as print is: a running header at the top, the body's lines,
    /// a running footer with the page number at the bottom.
    /// </summary>
    /// <param name="header">The header line of every page.</param>
    /// <param name="body">Each page's body lines.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Book(string header, IReadOnlyList<IReadOnlyList<string>> body)
    {
        var contents = body.Select((lines, index) =>
            Block(9, 72, 760, 12, [header]) + "\n" +
            Block(11, 72, 720, 14, lines) + "\n" +
            Block(9, 280, 40, 12, [Invariant($"Page {index + 1} of {body.Count}")])).ToList();
        return Pages(contents);
    }

    /// <summary>
    /// One page holding as many lines as asked in a one-point font (a text layer as dense as a
    /// page can hold): a page longer than a document may be.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The PDF.</returns>
    public static byte[] DensePage(IReadOnlyList<string> lines)
    {
        return Pages([Block(1, 36, 780, 1.4, lines)]);
    }

    /// <summary>Prose lines of an English-looking text, distinct sentences, a fixed seed: the body of a book's pages.</summary>
    /// <param name="random">The seeded source.</param>
    /// <param name="lines">How many lines.</param>
    /// <param name="wordsPerLine">Words on a line.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Prose(Random random, int lines, int wordsPerLine)
    {
        var result = new List<string>(lines);
        var inSentence = 0;
        for (var line = 0; line < lines; line++)
        {
            var builder = new StringBuilder();
            for (var word = 0; word < wordsPerLine; word++)
            {
                var text = WORDS[random.Next(WORDS.Length)];
                if (inSentence == 0)
                    text = char.ToUpperInvariant(text[0]) + text[1..];
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(text);
                inSentence++;
                if (inSentence >= 9 + random.Next(8))
                {
                    builder.Append('.');
                    inSentence = 0;
                }
            }

            result.Add(builder.ToString());
        }

        return result;
    }

    #endregion

    #region Tools

    private static byte[] Pages(IReadOnlyList<string> contents)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var kids = new StringBuilder();
        foreach (var content in contents)
        {
            var stream = writer.AddStream("", Latin1(content));
            kids.Append(Invariant($"{writer.Add(Page(tree, font, stream))} 0 R "));
        }

        writer.Set(catalog, Invariant($"<< /Type /Catalog /Pages {tree} 0 R >>"));
        writer.Set(tree, Invariant($"<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {contents.Count} >>"));
        return writer.Build(catalog);
    }

    private static string Page(int tree, int font, int content)
    {
        return Invariant($"<< /Type /Page /Parent {tree} 0 R {PAGE_BOX} /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
    }

    /// <summary>A text block: the lines one under the other from (x, y), each a string drawn with Tj.</summary>
    private static string Block(double size, double x, double y, double leading, IReadOnlyList<string> lines)
    {
        var builder = new StringBuilder(Invariant($"BT /F1 {size} Tf {x} {y} Td"));
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
                builder.Append(Invariant($" 0 {-leading} Td"));
            builder.Append(" (").Append(Escape(lines[index])).Append(") Tj");
        }

        return builder.Append(" ET").ToString();
    }

    private static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static (byte[] Owner, byte[] User, byte[] FileKey) StandardSecurity(byte[] userPassword, byte[] ownerPassword)
    {
        var ownerKey = Md5Rounds(MD5.HashData(Padded(ownerPassword)));
        var ownerEntry = Rc4Rounds(ownerKey, Padded(userPassword));
        var permissions = BitConverter.GetBytes(PERMISSIONS);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(permissions);

        var fileKey = Md5Rounds(MD5.HashData([.. Padded(userPassword), .. ownerEntry, .. permissions, .. FILE_ID]));
        var userEntry = Rc4Rounds(fileKey, MD5.HashData([.. PASSWORD_PADDING, .. FILE_ID])).Concat(new byte[16]).ToArray();
        return (ownerEntry, userEntry, fileKey);
    }

    private static byte[] ObjectKey(byte[] fileKey, int number)
    {
        return MD5.HashData([.. fileKey, (byte)number, (byte)(number >> 8), (byte)(number >> 16), 0, 0])[..16];
    }

    private static byte[] Padded(byte[] password)
    {
        return password.Concat(PASSWORD_PADDING).Take(32).ToArray();
    }

    private static byte[] Md5Rounds(byte[] digest)
    {
        for (var round = 0; round < 50; round++)
            digest = MD5.HashData(digest[..16]);

        return digest[..16];
    }

    /// <summary>RC4 with the key, then nineteen more times with the key's bytes XOR 1..19 (revision 3).</summary>
    private static byte[] Rc4Rounds(byte[] key, byte[] data)
    {
        data = Rc4(key, data);
        for (var round = 1; round < 20; round++)
            data = Rc4(key.Select(value => (byte)(value ^ round)).ToArray(), data);

        return data;
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var state = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[data.Length];
        for (int index = 0, i = 0, j = 0; index < data.Length; index++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
            output[index] = (byte)(data[index] ^ state[(state[i] + state[j]) & 0xFF]);
        }

        return output;
    }

    private static byte[] Latin1(string text)
    {
        return Encoding.Latin1.GetBytes(text);
    }

    private static string Invariant(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }

    #endregion
}
