using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// The job report: every shard's verdict, blobs and content hash in task order, and the
/// aggregate — documents in and out, bytes moved, the drop and language histograms summed
/// over the fleet. Schema <c>corpus-report/1</c>.
/// </summary>
public static class CorpusReportWriter
{
    #region Constants

    /// <summary>The report schema name.</summary>
    public const string SCHEMA = "corpus-report/1";

    #endregion

    #region Functions

    /// <summary>Writes the report.</summary>
    /// <param name="results">The shard results, any order.</param>
    /// <returns>UTF-8 JSON.</returns>
    public static byte[] Write(IReadOnlyList<CorpusResultData> results)
    {
        var ordered = results.OrderBy(result => result.TaskIndex).ToList();
        var drops = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var languages = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        long recordsIn = 0, extracted = 0, documentsOut = 0, fetched = 0, produced = 0;
        foreach (var result in ordered)
        {
            verdicts[result.Verdict.ToString()] = verdicts.GetValueOrDefault(result.Verdict.ToString()) + 1;
            if (result.Stats == null)
                continue;

            recordsIn += result.Stats.RecordsIn;
            extracted += result.Stats.DocumentsExtracted;
            documentsOut += result.Stats.DocumentsOut;
            fetched += result.Stats.BytesFetched;
            produced += result.Stats.BytesProduced;
            foreach (var drop in result.Stats.Drops)
                drops[drop.Name] = drops.GetValueOrDefault(drop.Name) + drop.Count;
            foreach (var language in result.Stats.Languages)
                languages[language.Name] = languages.GetValueOrDefault(language.Name) + language.Count;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ControllerBuildInfo.VERSION);

            writer.WriteStartObject("summary");
            writer.WriteNumber("tasks", ordered.Count);
            writer.WriteStartObject("verdicts");
            foreach (var pair in verdicts)
                writer.WriteNumber(pair.Key, pair.Value);
            writer.WriteEndObject();
            writer.WriteNumber("records_in", recordsIn);
            writer.WriteNumber("documents_extracted", extracted);
            writer.WriteNumber("documents_out", documentsOut);
            writer.WriteNumber("bytes_fetched", fetched);
            writer.WriteNumber("bytes_produced", produced);
            WriteCounts(writer, "drops", drops);
            WriteCounts(writer, "languages", languages);
            writer.WriteEndObject();

            writer.WriteStartArray("shards");
            foreach (var result in ordered)
                WriteShard(writer, result);
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteShard(Utf8JsonWriter writer, CorpusResultData result)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", result.TaskIndex);
        writer.WriteString("url", result.SourceUrl);
        writer.WriteString("verdict", result.Verdict.ToString());
        writer.WriteString("text_blob", result.TextBlobId == Guid.Empty ? "" : result.TextBlobId.ToString("D"));
        writer.WriteString("signatures_blob", result.SignaturesBlobId == Guid.Empty ? "" : result.SignaturesBlobId.ToString("D"));
        writer.WriteString("content_sha256", result.ContentSha256);
        writer.WriteString("error", result.Error);
        writer.WriteString("node", result.NodeId == Guid.Empty ? "" : result.NodeId.ToString("D"));
        writer.WriteString("node_name", result.NodeName);
        writer.WriteString("controller", result.ControllerVersion);

        if (result.Stats is { } stats)
        {
            writer.WriteStartObject("stats");
            writer.WriteNumber("records_in", stats.RecordsIn);
            writer.WriteNumber("documents_extracted", stats.DocumentsExtracted);
            writer.WriteNumber("documents_out", stats.DocumentsOut);
            writer.WriteNumber("bytes_fetched", stats.BytesFetched);
            writer.WriteNumber("bytes_produced", stats.BytesProduced);
            writer.WriteNumber("fetch_ms", stats.FetchMs);
            writer.WriteNumber("process_ms", stats.ProcessMs);
            WriteCounts(writer, "drops", stats.Drops.ToDictionary(count => count.Name, count => (long)count.Count, StringComparer.Ordinal));
            WriteCounts(writer, "languages", stats.Languages.ToDictionary(count => count.Name, count => (long)count.Count, StringComparer.Ordinal));
            writer.WriteEndObject();
        }
        else
            writer.WriteNull("stats");

        writer.WriteEndObject();
    }

    private static void WriteCounts(Utf8JsonWriter writer, string name, IReadOnlyDictionary<string, long> counts)
    {
        writer.WriteStartObject(name);
        foreach (var pair in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            writer.WriteNumber(pair.Key, pair.Value);
        writer.WriteEndObject();
    }

    #endregion
}
