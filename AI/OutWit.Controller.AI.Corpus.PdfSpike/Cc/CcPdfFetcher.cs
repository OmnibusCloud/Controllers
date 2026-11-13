using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OutWit.Controller.AI.Corpus.PdfSpike.Cc;

/// <summary>
/// Fetches the PDF responses a list names from one Common Crawl WARC file, each by a range
/// request for its gzip member, and keeps the HTTP body when its SHA-256 matches the list.
/// The documents themselves are never committed: the list holds offsets and hashes only.
/// </summary>
public static class CcPdfFetcher
{
    #region Constants

    private const string ORIGIN = "https://data.commoncrawl.org/";
    private const int ATTEMPTS = 6;

    #endregion

    #region Functions

    /// <summary>Fetches every document of the list into a folder.</summary>
    /// <param name="listPath">The list (cc-pdfs.json).</param>
    /// <param name="folder">The output folder.</param>
    /// <returns>The number of documents that failed.</returns>
    public static async Task<int> FetchAsync(string listPath, string folder)
    {
        Directory.CreateDirectory(folder);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(listPath));
        var warc = document.RootElement.GetProperty("warc").GetString()!;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OutWit-Corpus-PdfSpike/0.1");

        var failed = 0;
        foreach (var entry in document.RootElement.GetProperty("documents").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var target = Path.Combine(folder, name);
            var sha256 = entry.GetProperty("sha256").GetString()!;
            if (File.Exists(target) && Hash(await File.ReadAllBytesAsync(target)) == sha256)
                continue;

            var body = await FetchBodyAsync(http, warc, entry.GetProperty("offset").GetInt64(), entry.GetProperty("length").GetInt64());
            if (body == null || Hash(body) != sha256)
            {
                failed++;
                Console.Error.WriteLine($"  {name}: {(body == null ? "not fetched" : "hash differs")}");
                continue;
            }

            await File.WriteAllBytesAsync(target, body);
        }

        return failed;
    }

    private static async Task<byte[]?> FetchBodyAsync(HttpClient http, string warc, long offset, long length)
    {
        for (var attempt = 1; attempt <= ATTEMPTS; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ORIGIN + warc);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
                using var response = await http.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.PartialContent)
                    return Body(await response.Content.ReadAsByteArrayAsync());
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt * attempt));
        }

        return null;
    }

    private static byte[] Body(byte[] member)
    {
        using var inflated = new MemoryStream();
        using (var gzip = new GZipStream(new MemoryStream(member), CompressionMode.Decompress))
            gzip.CopyTo(inflated);

        var record = inflated.ToArray();
        var warcEnd = IndexOf(record, "\r\n\r\n"u8, 0);
        var headers = Encoding.Latin1.GetString(record, 0, warcEnd);
        var length = long.Parse(headers.Split("\r\n").First(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
        var payloadStart = warcEnd + 4;
        var httpEnd = IndexOf(record, "\r\n\r\n"u8, payloadStart);
        var bodyStart = httpEnd + 4;
        return record.AsSpan(bodyStart, (int)(payloadStart + length - bodyStart)).ToArray();
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern, int from)
    {
        var at = data.AsSpan(from).IndexOf(pattern);
        return at < 0 ? throw new InvalidDataException("no header end in the WARC record") : from + at;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion
}
