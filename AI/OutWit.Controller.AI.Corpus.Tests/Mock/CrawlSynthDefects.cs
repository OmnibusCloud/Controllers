using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The generated crawl as an origin or a link can spoil it: the same records with a defect put
/// in, so that what the pipeline makes of a damaged shard is asserted on a shard of the size and
/// the mix of the tests' own, not on three records.
/// </summary>
public static class CrawlSynthDefects
{
    #region Constants

    private const string DATE = "2026-10-05T10:56:23Z";

    #endregion

    #region Functions

    /// <summary>
    /// A shard that ends inside a record's payload: so many whole records, then the next
    /// record's headers and the first bytes of its payload. Unpacked (a reader takes a plain
    /// WARC stream as it takes a gzip one), so that the cut is where it is said to be.
    /// </summary>
    /// <param name="records">The shard's records.</param>
    /// <param name="whole">How many records are whole.</param>
    /// <param name="payloadBytes">How many bytes of the next record's payload are there (fewer than it has).</param>
    /// <returns>The shard's bytes, unpacked.</returns>
    public static byte[] CutInsideAPayload(IReadOnlyList<byte[]> records, int whole, int payloadBytes)
    {
        var next = records[whole];
        var headers = next.AsSpan().IndexOf("\r\n\r\n"u8) + 4;
        if (headers < 4 || headers + payloadBytes >= next.Length - 4)
            throw new ArgumentOutOfRangeException(nameof(payloadBytes), $"Record {whole} has {next.Length - headers - 4} bytes of payload.");

        return [.. records.Take(whole).SelectMany(record => record), .. next.AsSpan(0, headers + payloadBytes)];
    }

    /// <summary>
    /// A shard that ends inside a record's headers: so many whole records, then the first bytes
    /// of the next record's headers, cut in the middle of a line or after one. Unpacked, as
    /// <see cref="CutInsideAPayload"/> is.
    /// </summary>
    /// <param name="records">The shard's records.</param>
    /// <param name="whole">How many records are whole.</param>
    /// <param name="headerBytes">How many bytes of the next record's headers are there (at least one, fewer than they have).</param>
    /// <returns>The shard's bytes, unpacked.</returns>
    public static byte[] CutInsideAHeader(IReadOnlyList<byte[]> records, int whole, int headerBytes)
    {
        var next = records[whole];
        var headers = next.AsSpan().IndexOf("\r\n\r\n"u8) + 4;
        if (headerBytes < 1 || headerBytes >= headers)
            throw new ArgumentOutOfRangeException(nameof(headerBytes), $"Record {whole} has {headers} bytes of headers.");

        return [.. records.Take(whole).SelectMany(record => record), .. next.AsSpan(0, headerBytes)];
    }

    /// <summary>
    /// Records that are no page, put between the pages: a request, a metadata record, a record
    /// of a type no reader knows. A WET file has none of them; a file that does is still a WARC
    /// file.
    /// </summary>
    /// <param name="records">The shard's records.</param>
    /// <param name="every">A group of strangers goes after every so many records.</param>
    /// <returns>The records with the strangers among them, and how many strangers there are.</returns>
    public static (IReadOnlyList<byte[]> Records, int Strangers) WithStrangers(IReadOnlyList<byte[]> records, int every)
    {
        var mixed = new List<byte[]>(records.Count + records.Count / every * 3 + 3);
        var strangers = 0;
        for (var index = 0; index < records.Count; index++)
        {
            mixed.Add(records[index]);
            if ((index + 1) % every != 0)
                continue;

            var uri = $"https://stranger.example/{index}";
            mixed.Add(Record("request", uri, "application/http; msgtype=request", Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: stranger.example\r\n\r\n")));
            mixed.Add(Record("metadata", uri, "application/warc-fields", Encoding.ASCII.GetBytes("fetchTimeMs: 7\r\n")));
            mixed.Add(Record("x-unheard-of", uri, "application/octet-stream", [0, 1, 2, 3, 0xFF, 0xFE]));
            strangers += 3;
        }

        return (mixed, strangers);
    }

    /// <summary>A gzip member that holds no WARC record, in the place of one that did.</summary>
    /// <param name="records">The shard's records.</param>
    /// <param name="at">The record that is replaced.</param>
    /// <returns>The records with the noise among them.</returns>
    public static IReadOnlyList<byte[]> WithNoise(IReadOnlyList<byte[]> records, int at)
    {
        var noisy = records.ToList();
        noisy[at] = Encoding.ASCII.GetBytes("<html><body>503 Service Unavailable: the origin answered a page where a record should be</body></html>\r\n\r\n");
        return noisy;
    }

    private static byte[] Record(string type, string uri, string contentType, byte[] payload)
    {
        var header = Encoding.ASCII.GetBytes($"WARC/1.0\r\nWARC-Type: {type}\r\nWARC-Target-URI: {uri}\r\nWARC-Date: {DATE}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\n\r\n");
        return [.. header, .. payload, .. "\r\n\r\n"u8];
    }

    #endregion
}
