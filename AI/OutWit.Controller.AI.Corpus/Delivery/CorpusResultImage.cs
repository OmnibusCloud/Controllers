using System.Runtime.CompilerServices;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Delivery;

/// <summary>
/// The walk of a result's image, made before the decoder is handed a byte of it. A node chose
/// every byte of a result frame, and a decoder that believes a length it reads can be led outside
/// its buffer by one; so the image is walked here first, member by member in the order the wire
/// has them, and the decoder sees only an image in which
/// <list type="bullet">
/// <item>every object states exactly the members its type has (a null where the type allows one);</item>
/// <item>every string's lengths are inside the bytes that remain, agree with one another, and are
/// within the most characters a string of a result may have;</item>
/// <item>every list's count is within the most its kind may hold (the drop rules the stages have,
/// the languages a pack may carry, the keys of the unsupported breakdown);</item>
/// <item>nothing is left over after the last member.</item>
/// </list>
/// What the members say is not judged here: that is <see cref="CorpusResultCheck"/>'s, on the
/// decoded result. The walk follows the layout of <see cref="CorpusResultData"/> and the types it
/// holds; a member appended to one of them is appended here, and the tests hold the two together.
/// </summary>
public static class CorpusResultImage
{
    #region Constants

    /// <summary>The most characters any string of a result may have: those of a source URL, the longest thing a result says.</summary>
    public const int MAX_TEXT_CHARS = CorpusResultCheck.MAX_URL_CHARS;

    /// <summary>The members of <see cref="CorpusResultData"/> on the wire.</summary>
    public const int RESULT_MEMBERS = 17;

    /// <summary>The members of <see cref="CorpusShardStatsData"/> on the wire.</summary>
    public const int STATS_MEMBERS = 12;

    /// <summary>The members of <see cref="CorpusCountData"/> on the wire.</summary>
    public const int COUNT_MEMBERS = 2;

    /// <summary>The members of <see cref="CorpusPdfStatsData"/> on the wire.</summary>
    public const int PDF_MEMBERS = 8;

    /// <summary>The members of <see cref="CorpusMinHashData"/> on the wire.</summary>
    public const int MINHASH_MEMBERS = 3;

    private const int INT_BYTES = sizeof(int);
    private const int LONG_BYTES = sizeof(long);

    private static readonly int GUID_BYTES = Unsafe.SizeOf<Guid>();
    private static readonly int VERDICT_BYTES = Unsafe.SizeOf<CorpusShardVerdict>();
    private static readonly int OUTPUT_BYTES = Unsafe.SizeOf<CorpusOutputMode>();

    #endregion

    #region Functions

    /// <summary>Walks the image of a result.</summary>
    /// <param name="image">The bytes of one result frame, whole.</param>
    /// <returns>True when the image is laid out as a result is and every head in it is within its bounds; false for anything else, a null result included.</returns>
    public static bool IsSound(ReadOnlySpan<byte> image)
    {
        var cursor = new CorpusImageCursor(image);
        if (!cursor.Object(RESULT_MEMBERS))
            return false;

        cursor.Skip(INT_BYTES);                 // TaskIndex
        cursor.Skip(VERDICT_BYTES);             // Verdict
        cursor.Skip(GUID_BYTES);                // TextBlobId
        cursor.Skip(GUID_BYTES);                // SignaturesBlobId
        cursor.Text(MAX_TEXT_CHARS);            // ContentSha256
        cursor.Text(MAX_TEXT_CHARS);            // Error
        Stats(ref cursor);                      // Stats
        cursor.Skip(GUID_BYTES);                // NodeId
        cursor.Text(MAX_TEXT_CHARS);            // NodeName
        cursor.Text(MAX_TEXT_CHARS);            // ControllerVersion
        cursor.Text(MAX_TEXT_CHARS);            // SourceUrl
        cursor.Text(MAX_TEXT_CHARS);            // RuntimeTarget
        cursor.Skip(INT_BYTES);                 // Degree
        cursor.Skip(OUTPUT_BYTES);              // Output

        if (cursor.Object(MINHASH_MEMBERS))     // MinHash
            cursor.Skip(3 * INT_BYTES);         // Hashes, ShingleWords, Bands

        cursor.Text(MAX_TEXT_CHARS);            // SignaturesSha256
        cursor.Text(MAX_TEXT_CHARS);            // Ticket

        return cursor.Done;
    }

    #endregion

    #region Tools

    private static void Stats(ref CorpusImageCursor cursor)
    {
        if (!cursor.Object(STATS_MEMBERS))
            return;

        cursor.Skip(3 * INT_BYTES);                             // RecordsIn, DocumentsExtracted, DocumentsOut
        Counts(ref cursor, CorpusResultCheck.MaxDrops);         // Drops
        Counts(ref cursor, CorpusResultCheck.MAX_LANGUAGES);    // Languages
        cursor.Skip(2 * LONG_BYTES);                            // BytesFetched, BytesProduced
        cursor.Skip(3 * INT_BYTES);                             // FetchMs, ProcessMs, WaitMs
        Counts(ref cursor, CorpusResultCheck.MAX_UNSUPPORTED);  // UnsupportedLanguages

        if (cursor.Object(PDF_MEMBERS))                         // Pdf
            cursor.Skip(7 * INT_BYTES + LONG_BYTES);            // In, Truncated, Extracted, Pages, Documents, PagesCut, RunningLines, Fuel
    }

    private static void Counts(ref CorpusImageCursor cursor, int maxItems)
    {
        for (var left = cursor.List(maxItems); left > 0 && cursor.Sound; left--)
        {
            if (!cursor.Object(COUNT_MEMBERS))
                continue;

            cursor.Text(MAX_TEXT_CHARS);    // Name
            cursor.Skip(INT_BYTES);         // Count
        }
    }

    #endregion
}
