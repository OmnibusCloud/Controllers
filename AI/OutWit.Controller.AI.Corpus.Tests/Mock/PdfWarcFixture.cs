using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A WARC shard of HTML and PDF responses written by our own code (<see cref="PdfSynth"/>,
/// <see cref="WarcSynth"/>), the way Common Crawl stores them: a page of HTML; a twelve-page book
/// with a running header and footer that splits into two page-range documents; the same kind of
/// book cut short by the crawler (<c>WARC-Truncated</c>); a PDF that needs a password and one
/// encrypted with the empty password; a page denser than a document may be; a PDF known only by
/// Common Crawl's identification; a PDF with no page, one with only empty pages, one that is not
/// readable; a "PDF" that is HTML; a PDF behind a 404; a Russian and a Greek PDF whose words are
/// broken at every line end. Every byte of the shard but the record ids is a function of this
/// code; the product does not read the ids.
/// </summary>
public static class PdfWarcFixture
{
    #region Constants

    /// <summary>The host of every URL in the shard.</summary>
    public const string HOST = "https://fixture.example/";

    /// <summary>The book's running header.</summary>
    public const string HEADER = "The River Valley Chronicle";

    /// <summary>The PDF responses with status 200 whose body starts as a PDF does: what the stage takes.</summary>
    public const int PDF_CANDIDATES = 12;

    #endregion

    #region Functions

    /// <summary>The shard, gzip member per record.</summary>
    /// <returns>The shard's bytes.</returns>
    public static byte[] Shard()
    {
        return WarcSynth.Shard(Records());
    }

    /// <summary>The shard's records that are not PDFs the stage takes: the page of HTML, the untyped PDF, the fake one, the 404.</summary>
    /// <returns>The shard's bytes.</returns>
    public static byte[] ShardWithoutPdfs()
    {
        var records = Records();
        return WarcSynth.Shard([records[0], records[10], records[11], records[12]]);
    }

    /// <summary>The Russian PDF: two pages of prose, a word broken at every line end.</summary>
    /// <returns>The PDF and the broken words as they read joined.</returns>
    public static (byte[] Pdf, IReadOnlyList<string> Broken) Russian()
    {
        var random = new Random(31);
        var first = PdfSynth.HyphenatedProse("Cyrillic", random, 24, 9);
        var second = PdfSynth.HyphenatedProse("Cyrillic", random, 24, 9);
        return (PdfSynth.UnicodeDocument([first.Lines, second.Lines]), [.. first.Broken, .. second.Broken]);
    }

    /// <summary>The Greek PDF: one page of prose, a word broken at every line end.</summary>
    /// <returns>The PDF and the broken words as they read joined.</returns>
    public static (byte[] Pdf, IReadOnlyList<string> Broken) Greek()
    {
        var prose = PdfSynth.HyphenatedProse("Greek", new Random(32), 20, 9);
        return (PdfSynth.UnicodeDocument([prose.Lines]), prose.Broken);
    }

    /// <summary>The records in shard order.</summary>
    /// <returns>The records.</returns>
    public static IReadOnlyList<byte[]> Records()
    {
        var book = Book(seed: 21, pages: 12);
        var cutBook = Book(seed: 22, pages: 12);
        var open = PdfSynth.Encrypted("", string.Join(' ', PdfSynth.Prose(new Random(24), 6, 12)));

        return
        [
            WarcSynth.HtmlResponseRecord(HOST + "page.html", "<html><head><title>The valley</title></head><body><p>" + WarcSynth.Paragraphs(new Random(11), 300) + "</p></body></html>"),
            WarcSynth.PdfResponseRecord(HOST + "chronicle.pdf", book),
            WarcSynth.PdfResponseRecord(HOST + "chronicle-cut.pdf", cutBook[..(cutBook.Length * 3 / 5)], truncated: true),
            WarcSynth.PdfResponseRecord(HOST + "locked.pdf", PdfSynth.Encrypted("a user password", "This page needs a password.")),
            WarcSynth.PdfResponseRecord(HOST + "open.pdf", open),
            WarcSynth.PdfResponseRecord(HOST + "dense.pdf", PdfSynth.DensePage(PdfSynth.Prose(new Random(23), 450, 12))),
            WarcSynth.PdfResponseRecord(HOST + "report.bin", PdfSynth.Document([PdfSynth.Prose(new Random(25), 30, 12), PdfSynth.Prose(new Random(26), 30, 12)]), contentType: "application/octet-stream"),
            WarcSynth.PdfResponseRecord(HOST + "empty-tree.pdf", PdfSynth.NoPages()),
            WarcSynth.PdfResponseRecord(HOST + "blank.pdf", PdfSynth.Document([[], []])),
            WarcSynth.PdfResponseRecord(HOST + "broken.pdf", Encoding.Latin1.GetBytes("%PDF-1.4\nthis is not really a PDF after its first line\n")),
            WarcSynth.PdfResponseRecord(HOST + "untyped.pdf", PdfSynth.Document([["A PDF whose server says nothing about its type and Common Crawl did not identify it."]]), contentType: "application/octet-stream", identifiedType: null),
            WarcSynth.PdfResponseRecord(HOST + "fake.pdf", Encoding.UTF8.GetBytes("<html><body><p>Not a PDF at all.</p></body></html>")),
            NotFound(HOST + "gone.pdf"),
            // A PDF typed by its server alone, after the ones that drop.
            WarcSynth.PdfResponseRecord(HOST + "notes.pdf", PdfSynth.Document([PdfSynth.Prose(new Random(27), 20, 12)]), identifiedType: null),
            WarcSynth.PdfResponseRecord(HOST + "letopis.pdf", Russian().Pdf),
            WarcSynth.PdfResponseRecord(HOST + "chroniko.pdf", Greek().Pdf)
        ];
    }

    /// <summary>A book of so many pages: a running header, forty lines of twelve words, a running footer.</summary>
    /// <param name="seed">The prose's seed.</param>
    /// <param name="pages">Its pages.</param>
    /// <returns>The PDF.</returns>
    public static byte[] Book(int seed, int pages)
    {
        var random = new Random(seed);
        return PdfSynth.Book(HEADER, Enumerable.Range(0, pages).Select(_ => PdfSynth.Prose(random, 40, 12)).ToList());
    }

    private static byte[] NotFound(string uri)
    {
        var body = Encoding.ASCII.GetBytes("%PDF-1.7 not here");
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 404 Not Found\r\nContent-Type: application/pdf\r\nContent-Length: {body.Length}\r\n\r\n");
        return WarcSynth.Record("response", uri, "application/http; msgtype=response", [.. head, .. body]);
    }

    #endregion
}
