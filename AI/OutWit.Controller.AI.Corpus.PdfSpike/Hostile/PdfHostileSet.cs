using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Hostile;

/// <summary>
/// The hostile set: PDFs written byte by byte to hurt a parser — decompression bombs in each
/// kind of stream a text extractor decodes, nesting, reference cycles, page-tree amplification,
/// broken and lying cross-reference data, truncation, pathological fonts and content streams.
/// Two sane documents are the baseline. Every file is a function of this code only.
/// </summary>
public static class PdfHostileSet
{
    #region Constants

    private const long GIB = 1L << 30;
    private const string HELVETICA = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    #endregion

    #region Functions

    /// <summary>Writes every case into a folder.</summary>
    /// <param name="folder">The output folder (created).</param>
    /// <returns>The file names in the order written.</returns>
    public static IReadOnlyList<string> Write(string folder)
    {
        Directory.CreateDirectory(folder);
        var names = new List<string>();

        void Save(string name, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(folder, name + ".pdf"), bytes);
            names.Add(name + ".pdf");
            Console.Error.WriteLine($"  {name}.pdf {bytes.Length:N0} bytes");
        }

        var spaces1G = Deflate(GIB, (byte)' ');

        Save("valid-hello", Document(1, page => "Hello hostile world, this is a valid page."));
        var hundred = Document(100, page => $"Page {page} of a valid document with a sentence on every page.");
        Save("valid-100p", hundred);

        Save("bomb-content-1g", SinglePage(writer => writer.AddStream("/Filter /FlateDecode", spaces1G), ""));
        Save("bomb-content-3g-double-flate", SinglePage(writer => writer.AddStream("/Filter [/FlateDecode /FlateDecode]", Deflate(Deflate(3 * GIB, (byte)' '))), ""));
        Save("bomb-tounicode-1g", BombToUnicode(spaces1G));
        Save("bomb-fontfile-1g", BombFontFile(Deflate(GIB, 0)));
        Save("bomb-image-1g", BombImage(Deflate(GIB, 0)));
        Save("bomb-objstm-1g", BombObjectStream());
        Save("bomb-lzw-1g", SinglePage(writer => writer.AddStream("/Filter /LZWDecode", LzwBomb(GIB)), ""));
        Save("bomb-predictor-columns", SinglePage(writer => writer.AddStream("/Filter /FlateDecode /DecodeParms << /Predictor 12 /Colors 32 /BitsPerComponent 16 /Columns 2000000000 >>", Deflate(Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 720 Td (predictor) Tj ET"))), ""));

        Save("deep-array-100k", SinglePage(writer => Content(writer, "BT /F1 12 Tf 72 720 Td (deep) Tj ET"), "/Deep " + new string('[', 100_000) + new string(']', 100_000)));
        Save("deep-dict-100k", SinglePage(writer => Content(writer, "BT /F1 12 Tf 72 720 Td (deep) Tj ET"), "/Deep " + Repeat("<< /A ", 100_000) + Repeat(">> ", 100_000)));
        Save("deep-content-array-100k", SinglePage(writer => Content(writer, "BT /F1 12 Tf 72 720 Td " + new string('[', 100_000) + new string(']', 100_000) + " TJ (after) Tj ET"), ""));
        Save("deep-q-1m", SinglePage(writer => writer.AddStream("/Filter /FlateDecode", Deflate(Encoding.Latin1.GetBytes(Repeat("q ", 1_000_000) + "BT /F1 12 Tf 72 720 Td (saved) Tj ET"))), ""));

        Save("cycle-self-contents", CycleSelfContents());
        Save("cycle-page-tree", CyclePageTree());
        Save("cycle-parent-resources", CycleParentResources());
        Save("cycle-form-self", FormCycle(pair: false));
        Save("cycle-form-pair", FormCycle(pair: true));
        Save("cycle-length-self", CycleLengthSelf());
        Save("amplify-form-fanout-16x6", FormFanout(16, 6));

        Save("pages-count-lie", PagesCountLie());
        Save("pages-fanout-1m", PagesFanout(1000, 1000));
        Save("pages-real-20k", Document(20_000, page => "p" + page));

        Save("xref-shifted-offsets", Rebuild3(writer => writer.Build(1, offsetShift: 37)));
        Save("xref-missing", Rebuild3(writer => writer.Build(1, writeXref: false)));
        Save("xref-startxref-garbage", Rebuild3(writer => writer.Build(1, startXref: 999_999_999)));
        Save("xref-stream-size-2g", XrefStreamHugeSize());
        Save("object-number-huge", ObjectNumberHuge());

        foreach (var percent in new[] { 10, 50, 90, 99 })
            Save($"truncated-{percent:D2}", hundred[..(int)(hundred.LongLength * percent / 100)]);

        Save("font-truetype-garbage-tables", FontTrueTypeGarbage());
        Save("font-cff-subr-recursion", FontCffSubrRecursion());
        Save("font-type3-recursive", FontType3Recursive());
        Save("content-5m-text-ops", SinglePage(writer => writer.AddStream("/Filter /FlateDecode", Deflate(Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 720 Td " + Repeat("(a) Tj ", 5_000_000) + "ET"))), ""));
        Save("content-inline-image-no-ei", SinglePage(writer => Content(writer, "BT /F1 12 Tf 72 720 Td (before) Tj ET\nBI /W 50000 /H 50000 /BPC 8 /CS /RGB ID\n" + Repeat("Q", 1 << 20)), ""));
        Save("encrypted-unknown-password", EncryptedUnknownPassword());
        Save("not-a-pdf", Encoding.Latin1.GetBytes(Repeat("<html><body>not a pdf</body></html>\n", 1000)));
        Save("empty-file", []);

        return names;
    }

    private static byte[] Document(int pages, Func<int, string> text)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var kids = new StringBuilder();

        for (var page = 1; page <= pages; page++)
        {
            var content = writer.AddStream("", Encoding.Latin1.GetBytes($"BT /F1 12 Tf 72 720 Td ({text(page)}) Tj ET"));
            var number = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
            kids.Append($"{number} 0 R ");
        }

        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{kids}] /Count {pages} >>");
        return writer.Build(catalog);
    }

    private static byte[] Rebuild3(Func<PdfRawWriter, byte[]> build)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var kids = new StringBuilder();
        for (var page = 1; page <= 3; page++)
        {
            var content = writer.AddStream("", Encoding.Latin1.GetBytes($"BT /F1 12 Tf 72 720 Td (Page {page} with broken cross references.) Tj ET"));
            var number = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
            kids.Append($"{number} 0 R ");
        }

        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{kids}] /Count 3 >>");
        return build(writer);
    }

    private static byte[] SinglePage(Func<PdfRawWriter, int> contents, string pageExtra, string? resources = null)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = contents(writer);
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources {resources ?? $"<< /Font << /F1 {font} 0 R >> >>"} /Contents {content} 0 R {pageExtra} >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static int Content(PdfRawWriter writer, string content)
    {
        return writer.AddStream("", Encoding.Latin1.GetBytes(content));
    }

    private static byte[] BombToUnicode(byte[] deflated)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var cmap = writer.AddStream("/Filter /FlateDecode", deflated);
        var font = writer.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode {cmap} 0 R >>");
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (to unicode bomb) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] BombFontFile(byte[] deflated)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var file = writer.AddStream("/Filter /FlateDecode /Length1 1073741824", deflated);
        var descriptor = writer.Add($"<< /Type /FontDescriptor /FontName /Bomb /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 {file} 0 R >>");
        var widths = string.Join(' ', Enumerable.Repeat("500", 95));
        var font = writer.Add($"<< /Type /Font /Subtype /TrueType /BaseFont /Bomb /FirstChar 32 /LastChar 126 /Widths [{widths}] /Encoding /WinAnsiEncoding /FontDescriptor {descriptor} 0 R >>");
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (font file bomb) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] BombImage(byte[] deflated)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var image = writer.AddStream("/Type /XObject /Subtype /Image /Width 32768 /Height 32768 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", deflated);
        var content = Content(writer, "q 612 0 0 792 0 0 cm /Im1 Do Q BT /F1 12 Tf 72 720 Td (image bomb, text after) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> /XObject << /Im1 {image} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] BombObjectStream()
    {
        // Objects 1 (catalog) and 2 (page tree) live in object stream 5, padded to 1 GiB after them.
        var writer = new PdfRawWriter();
        writer.Reserve();
        writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (object stream bomb) Tj ET");
        var objectStream = writer.Reserve();
        var page = writer.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");

        var catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        var tree = $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>";
        var header = $"1 0 2 {catalog.Length + 1} ";
        var body = Encoding.Latin1.GetBytes(catalog + "\n" + tree + "\n");
        var headerBytes = Encoding.Latin1.GetBytes(header);

        writer.SetStream(objectStream, $"/Type /ObjStm /N 2 /First {headerBytes.Length} /Filter /FlateDecode", Deflate(headerBytes.Concat(body).ToArray(), GIB, (byte)' '));
        return writer.BuildWithXrefStream(1, [(1, objectStream, 0), (2, objectStream, 1)]);
    }

    private static byte[] CycleSelfContents()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var contents = writer.Reserve();
        writer.Set(contents, $"{contents} 0 R");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {contents} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] CyclePageTree()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var inner = writer.Reserve();
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{inner} 0 R] /Count 1 >>");
        writer.Set(inner, $"<< /Type /Pages /Parent {tree} 0 R /Kids [{tree} 0 R {inner} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] CycleParentResources()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (inherited resources) Tj ET");
        var page = writer.Reserve();
        var middle = writer.Reserve();
        writer.Set(page, $"<< /Type /Page /Parent {middle} 0 R /MediaBox [0 0 612 792] /Contents {content} 0 R >>");
        writer.Set(middle, $"<< /Type /Pages /Parent {page} 0 R /Kids [{page} 0 R] /Count 1 >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] FormCycle(bool pair)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var first = writer.Reserve();
        var second = pair ? writer.Reserve() : first;
        writer.SetStream(first, $"/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> /XObject << /Fm {second} 0 R >> >>", Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 720 Td (form) Tj ET /Fm Do"));
        if (pair)
            writer.SetStream(second, $"/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> /XObject << /Fm {first} 0 R >> >>", Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 700 Td (pair) Tj ET /Fm Do"));

        var content = Content(writer, "/Fm Do");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> /XObject << /Fm {first} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] CycleLengthSelf()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = writer.Reserve();
        writer.Set(content, $"<< /Length {content} 0 R >>\nstream\nBT /F1 12 Tf 72 720 Td (length is itself) Tj ET\nendstream");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] FormFanout(int fanout, int depth)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var forms = Enumerable.Range(0, depth + 1).Select(_ => writer.Reserve()).ToArray();
        for (var level = 0; level < depth; level++)
            writer.SetStream(forms[level], $"/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /XObject << /Fm {forms[level + 1]} 0 R >> >>", Encoding.Latin1.GetBytes(Repeat("/Fm Do ", fanout)));

        writer.SetStream(forms[depth], $"/Type /XObject /Subtype /Form /BBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >>", Encoding.Latin1.GetBytes("BT /F1 12 Tf 72 720 Td (x) Tj ET"));
        var content = Content(writer, "/Fm Do");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> /XObject << /Fm {forms[0]} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] PagesCountLie()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (one page, two billion claimed) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 2147483647 >>");
        return writer.Build(catalog);
    }

    private static byte[] PagesFanout(int outer, int inner)
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var middle = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (the same page again) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {middle} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(middle, $"<< /Type /Pages /Parent {tree} 0 R /Kids [{Repeat($"{page} 0 R ", inner)}] /Count {inner} >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{Repeat($"{middle} 0 R ", outer)}] /Count {(long)outer * inner} >>");
        return writer.Build(catalog);
    }

    private static byte[] XrefStreamHugeSize()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (an xref stream that claims two billion objects) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.BuildWithXrefStream(catalog, [], declaredSize: 2_000_000_000);
    }

    private static byte[] ObjectNumberHuge()
    {
        const string head = "%PDF-1.7\n";
        const string catalog = "1 0 obj\n<< /Type /Catalog /Pages 2147483646 0 R >>\nendobj\n";
        const string tree = "2147483646 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n";
        var xref = head.Length + catalog.Length + tree.Length;
        return Encoding.Latin1.GetBytes(
            head + catalog + tree +
            $"xref\n0 2\n0000000000 65535 f \n{head.Length:D10} 00000 n \n2147483646 1\n{head.Length + catalog.Length:D10} 00000 n \n" +
            $"trailer\n<< /Size 2147483647 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static byte[] FontTrueTypeGarbage()
    {
        var font = new byte[12 + 16 * 4];
        font[0] = 0; font[1] = 1; font[2] = 0; font[3] = 0;
        font[4] = 0xFF; font[5] = 0xFF;
        var tags = new[] { "head", "loca", "glyf", "cmap" };
        for (var index = 0; index < tags.Length; index++)
        {
            var at = 12 + index * 16;
            Encoding.Latin1.GetBytes(tags[index]).CopyTo(font, at);
            font[at + 8] = 0; font[at + 9] = 0; font[at + 10] = 0; font[at + 11] = 12;
            font[at + 12] = 0x7F; font[at + 13] = 0xFF; font[at + 14] = 0xFF; font[at + 15] = 0xFF;
        }

        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var file = writer.AddStream($"/Length1 {font.Length}", font);
        var descriptor = writer.Add($"<< /Type /FontDescriptor /FontName /Garbage /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile2 {file} 0 R >>");
        var widths = string.Join(' ', Enumerable.Repeat("500", 95));
        var fontObject = writer.Add($"<< /Type /Font /Subtype /TrueType /BaseFont /Garbage /FirstChar 32 /LastChar 126 /Widths [{widths}] /Encoding /WinAnsiEncoding /FontDescriptor {descriptor} 0 R >>");
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (garbage truetype tables) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObject} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] FontCffSubrRecursion()
    {
        var cff = CffRecursive(glyphs: 230);
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var file = writer.AddStream("/Subtype /Type1C", cff);
        var descriptor = writer.Add($"<< /Type /FontDescriptor /FontName /Recursive /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 {file} 0 R >>");
        var widths = string.Join(' ', Enumerable.Repeat("500", 95));
        var font = writer.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /Recursive /FirstChar 32 /LastChar 126 /Widths [{widths}] /FontDescriptor {descriptor} 0 R >>");
        var content = Content(writer, "BT /F1 12 Tf 72 720 Td (recursive charstrings) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] FontType3Recursive()
    {
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Reserve();
        var glyph = writer.AddStream("", Encoding.Latin1.GetBytes("1000 0 d0 BT /T3 1 Tf (a) Tj ET"));
        writer.Set(font, $"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a {glyph} 0 R >> /Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] /Resources << /Font << /T3 {font} 0 R >> >> >>");
        var content = Content(writer, "BT /T3 12 Tf 72 720 Td (aaaa) Tj ET");
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /T3 {font} 0 R >> >> /Contents {content} 0 R >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog);
    }

    private static byte[] EncryptedUnknownPassword()
    {
        var owner = Hex(Enumerable.Range(0, 32).Select(index => (byte)(index * 7 + 3)));
        var user = Hex(Enumerable.Range(0, 32).Select(index => (byte)(index * 13 + 5)));
        var writer = new PdfRawWriter();
        var catalog = writer.Reserve();
        var tree = writer.Reserve();
        var font = writer.Add(HELVETICA);
        var content = writer.AddStream("", Enumerable.Range(0, 64).Select(index => (byte)(index * 31 + 17)).ToArray());
        var page = writer.Add($"<< /Type /Page /Parent {tree} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {font} 0 R >> >> /Contents {content} 0 R >>");
        var encrypt = writer.Add($"<< /Filter /Standard /V 2 /R 3 /Length 128 /O <{owner}> /U <{user}> /P -4 >>");
        writer.Set(catalog, $"<< /Type /Catalog /Pages {tree} 0 R >>");
        writer.Set(tree, $"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");
        return writer.Build(catalog, trailerExtra: $"/Encrypt {encrypt} 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] ");
    }

    /// <summary>
    /// A bare CFF font whose every charstring calls global subroutine 0, which calls itself:
    /// an interpreter without a depth limit recurses until its stack ends.
    /// </summary>
    private static byte[] CffRecursive(int glyphs)
    {
        static byte[] Index(IReadOnlyList<byte[]> items)
        {
            using var output = new MemoryStream();
            output.WriteByte((byte)(items.Count >> 8));
            output.WriteByte((byte)items.Count);
            if (items.Count == 0)
                return output.ToArray();

            output.WriteByte(4);
            var offset = 1;
            output.Write([0, 0, 0, 1]);
            foreach (var item in items)
            {
                offset += item.Length;
                output.Write([(byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset]);
            }

            foreach (var item in items)
                output.Write(item);

            return output.ToArray();
        }

        static byte[] Int32Operand(int value)
        {
            return [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }

        byte[] header = [1, 0, 4, 4];
        var name = Index([Encoding.Latin1.GetBytes("Recursive")]);
        var strings = Index([]);
        var globalSubrs = Index([[32, 29, 11]]);
        var charStrings = Index(Enumerable.Range(0, glyphs).Select(_ => new byte[] { 32, 29, 14 }).ToArray());
        byte[] privateDict = [139, 20, 139, 21];

        // Top DICT: CharStrings (17) and Private (18) with fixed-width operands, so its size is known up front.
        var topSize = 5 + 1 + 5 + 5 + 1;
        var topIndexSize = 2 + 1 + 8 + topSize;
        var charStringsOffset = header.Length + name.Length + topIndexSize + strings.Length + globalSubrs.Length;
        var privateOffset = charStringsOffset + charStrings.Length;
        var top = Int32Operand(charStringsOffset).Append((byte)17)
            .Concat(Int32Operand(privateDict.Length)).Concat(Int32Operand(privateOffset)).Append((byte)18).ToArray();
        var topIndex = Index([top]);

        return header.Concat(name).Concat(topIndex).Concat(strings).Concat(globalSubrs).Concat(charStrings).Concat(privateDict).ToArray();
    }

    /// <summary>
    /// An LZW stream that decodes to about <paramref name="target"/> bytes of 'a': after each
    /// clear, every code names the string one longer than the last (the decoder's KwKwK case),
    /// so ~3800 codes of up to 12 bits give ~7 MB.
    /// </summary>
    private static byte[] LzwBomb(long target)
    {
        var bits = new LzwBitWriter();
        long produced = 0;
        while (produced < target)
        {
            bits.Write(256, 9);
            bits.Write('a', 9);
            produced += 1;
            var length = 1;
            for (var code = 258; code < 4094 && produced < target; code++)
            {
                bits.Write(code, Width(code));
                length++;
                produced += length;
            }

            bits.Write(256, 12);
        }

        bits.Write(257, 9);
        return bits.ToArray();

        static int Width(int code)
        {
            // Each code here is read when the decoder's next free entry equals it; PDF's default
            // EarlyChange 1 widens the code one entry early.
            return code + 1 >= 2048 ? 12 : code + 1 >= 1024 ? 11 : code + 1 >= 512 ? 10 : 9;
        }
    }

    private static byte[] Deflate(long bytes, byte fill)
    {
        return Deflate([], bytes, fill);
    }

    private static byte[] Deflate(byte[] prefix, long padding, byte fill)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(prefix);
            var chunk = new byte[1 << 20];
            Array.Fill(chunk, fill);
            for (long written = 0; written < padding; written += chunk.Length)
                zlib.Write(chunk, 0, (int)Math.Min(chunk.Length, padding - written));
        }

        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        return Deflate(data, 0, 0);
    }

    private static string Repeat(string text, int times)
    {
        return new StringBuilder(text.Length * times).Insert(0, text, times).ToString();
    }

    private static string Hex(IEnumerable<byte> bytes)
    {
        return Convert.ToHexString(bytes.ToArray());
    }

    #endregion
}
