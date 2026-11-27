using System.Diagnostics;
using System.Text.Json;
using OutWit.Controller.AI.Proof.Spike.Precheck;
using ZstdSharp;

// PF-2 spike: the structural pre-check of an export, streaming (spec section 5.4).
//
//   Pf2Precheck <export.ndjson | export.ndjson.zst> [--lean-version V] [--lean-githash H]
//               [--theorems a,b] [--no-duplicates] [--sha256 | --expect-sha256 D] [--no-stop]
//               [--max-bytes N] [--max-lines N] [--max-line-bytes N] [--max-literal-digits N]
//
// Prints one JSON object (the verdict, the counts, the time and the memory) and exits 0 when the
// export is accepted, 1 when it is refused, 2 when it could not be read.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Pf2Precheck <export> [options]");
    return 2;
}

PrecheckOptions options;
try
{
    options = PrecheckOptions.Parse(args.Skip(1).ToArray());
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

var path = args[0];
var clock = Stopwatch.StartNew();
var compressed = path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase);
PrecheckRefusalException? refusal = null;
string? failure = null;
var check = new ExportPrecheck(options);
ExportLineReader? reader = null;
CountingStream? counting = null;
long fileBytes = 0;
string? digest = null;
try
{
    var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
    fileBytes = file.Length;
    Stream source = compressed ? new DecompressionStream(file, 1 << 20) : file;
    counting = new CountingStream(source, options.MaxBytes, options.ComputeSha256);
    reader = new ExportLineReader(counting, options.MaxLineBytes);
    try
    {
        while (reader.TryReadLine(out var line))
        {
            if (check.Lines >= options.MaxLines)
                throw new PrecheckRefusalException("too_large", $"more than {options.MaxLines} lines");
            check.CheckLine(line);
        }
        if (reader.MissingFinalNewline)
            throw new PrecheckRefusalException("json_invalid", $"line {check.Lines}: the export does not end with a newline (cut short?)");
        check.Finish();
    }
    finally
    {
        digest = counting.Digest();
        counting.Dispose();
    }
}
catch (PrecheckRefusalException error)
{
    refusal = error;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ZstdException)
{
    failure = error.Message;
}
clock.Stop();

if (refusal is null && failure is null && options.ExpectedSha256 is not null && digest != options.ExpectedSha256)
    refusal = new PrecheckRefusalException("digest_mismatch", $"SHA-256 {digest}, expected {options.ExpectedSha256}");

var process = Process.GetCurrentProcess();
process.Refresh();
var gc = GC.GetGCMemoryInfo();
var bytesRead = counting?.BytesRead ?? 0;
using (var output = Console.OpenStandardOutput())
using (var json = new Utf8JsonWriter(output))
{
    json.WriteStartObject();
    json.WriteString("tool", "pf2-precheck");
    json.WriteString("file", Path.GetFileName(path));
    json.WriteBoolean("compressed", compressed);
    json.WriteString("verdict", failure is not null ? "error" : refusal is null ? "accepted" : "refused");
    if (refusal is not null)
    {
        json.WriteString("code", refusal.Code);
        json.WriteString("detail", refusal.Detail);
    }
    if (failure is not null)
        json.WriteString("error", failure);
    json.WriteNumber("file_bytes", fileBytes);
    json.WriteNumber("bytes", bytesRead);
    json.WriteNumber("lines", check.Lines);
    json.WriteNumber("names", check.NameCount);
    json.WriteNumber("levels", check.LevelCount);
    json.WriteNumber("expressions", check.ExpressionCount);
    json.WriteNumber("constants", check.Constants);
    json.WriteStartObject("declarations");
    json.WriteNumber("theorems", check.Theorems);
    json.WriteNumber("definitions", check.Definitions);
    json.WriteNumber("axioms", check.Axioms);
    json.WriteNumber("opaques", check.Opaques);
    json.WriteNumber("quotients", check.Quotients);
    json.WriteNumber("inductive_blocks", check.InductiveBlocks);
    json.WriteNumber("inductive_types", check.InductiveTypes);
    json.WriteNumber("constructors", check.Constructors);
    json.WriteNumber("recursors", check.Recursors);
    json.WriteEndObject();
    json.WriteNumber("longest_line", reader?.LongestLine ?? 0);
    json.WriteNumber("largest_number_literal_digits", check.LargestNumberLiteral);
    json.WriteNumber("longest_string_literal_bytes", check.LongestStringLiteral);
    json.WriteNumber("max_term_depth", check.MaxTermDepth);
    json.WriteBoolean("duplicates_checked", options.CheckDuplicates);
    json.WriteNumber("duplicate_entries", check.DuplicateEntries);
    json.WriteStartObject("meta");
    json.WriteString("exporter", check.MetaExporter);
    json.WriteString("exporter_version", check.MetaExporterVersion);
    json.WriteString("format", check.MetaFormat);
    json.WriteString("lean", check.MetaLeanVersion);
    json.WriteString("githash", check.MetaLeanGithash);
    json.WriteEndObject();
    if (digest is not null)
        json.WriteString("sha256", digest);
    json.WriteNumber("seconds", Math.Round(clock.Elapsed.TotalSeconds, 2));
    json.WriteNumber("mb_per_s", clock.Elapsed.TotalSeconds > 0 ? Math.Round(bytesRead / 1e6 / clock.Elapsed.TotalSeconds, 1) : 0);
    json.WriteStartObject("memory");
    json.WriteNumber("peak_working_set", process.PeakWorkingSet64);
    json.WriteNumber("gc_heap", gc.HeapSizeBytes);
    json.WriteNumber("gc_total_committed", gc.TotalCommittedBytes);
    json.WriteNumber("fingerprints", check.FingerprintCount);
    json.WriteNumber("fingerprint_bytes", check.FingerprintBytes);
    json.WriteNumber("name_table_bytes", check.NameTableBytes);
    json.WriteNumber("depth_table_bytes", check.DepthTableBytes);
    json.WriteNumber("line_buffer_bytes", reader?.BufferBytes ?? 0);
    json.WriteEndObject();
    json.WriteEndObject();
}
Console.WriteLine();
return failure is not null ? 2 : refusal is null ? 0 : 1;
