using System.Globalization;
using System.Text.Json;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Live;

/// <summary>
/// Drives the bundled Corpus scripts on a real server through the public SDK: uploads the
/// manifest (and the language model), submits CorpusPreflight or CorpusProcess, prints the
/// report and the integrity document, and compares the content hashes of repeated runs —
/// the fleet-level proof that the same shard gives the same product on any node.
/// <c>diagnostics</c> runs CorpusRuntimeDiagnostics (the server's module) and prints the
/// capabilities document.
/// </summary>
public static class Program
{
    #region Constants

    private const string SERVER_ENV = "WITAI_CORPUS_LIVE_SERVER";
    private const string IDENTITY_ENV = "WITAI_CORPUS_LIVE_IDENTITY";
    private const string KEY_ENV = "WITAI_VERIFY_LIVE_KEY";

    private const string DEFAULT_SERVER = "https://test.omnibuscloud.com";
    private const string DEFAULT_IDENTITY = "https://auth.omnibuscloud.com";

    private const string PROCESS_SCRIPT = "CorpusProcess";
    private const string PREFLIGHT_SCRIPT = "CorpusPreflight";
    private const string DEDUP_SCRIPT = "CorpusDedup";
    private const string DIAGNOSTICS_SCRIPT = "CorpusRuntimeDiagnostics";

    private const string REPORT = "report";
    private const string INTEGRITY = "integrity";
    private const string RESULT = "result";
    private const string DEDUP = "dedup";
    private const string CAPABILITIES = "capabilities";

    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

    private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(5);

    #endregion

    #region Entry

    public static async Task<int> Main(string[] args)
    {
        var diagnostics = args.Length >= 1 && args[0] == "diagnostics";
        if ((args.Length < 2 && !diagnostics) || args.Length == 0 || args[0] is "-h" or "--help")
            return Usage();

        var key = Environment.GetEnvironmentVariable(KEY_ENV);
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine($"{KEY_ENV} is not set (a wit_sk_ application key).");
            return 2;
        }

        var manifestPath = diagnostics ? "" : args[1];
        if (!diagnostics && !File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"manifest not found: {manifestPath}");
            return 2;
        }

        var modelPath = OptionValue(args, "--model", null);
        if (modelPath != null && !File.Exists(modelPath))
        {
            Console.Error.WriteLine($"model not found: {modelPath}");
            return 2;
        }

        var server = Environment.GetEnvironmentVariable(SERVER_ENV) ?? DEFAULT_SERVER;
        var identity = Environment.GetEnvironmentVariable(IDENTITY_ENV) ?? DEFAULT_IDENTITY;
        var options = new RunOptions(
            OptionValue(args, "--runs", 1),
            OptionValue(args, "--sampling", 0.0),
            OptionValue(args, "--max-shard-mb", 0),
            OptionValue(args, "--save", null),
            OptionValue(args, "--threshold", 0.0));

        Console.WriteLine($"server   : {server}");
        Console.WriteLine($"identity : {identity}");
        if (!diagnostics)
        {
            Console.WriteLine($"input    : {Path.GetFileName(manifestPath)}, {new FileInfo(manifestPath).Length} byte(s)");
            Console.WriteLine($"model    : {(modelPath == null ? "(none)" : $"{Path.GetFileName(modelPath)}, {new FileInfo(modelPath).Length} byte(s)")}");
        }

        await using var client = new WitCloudClient(server, identity, key);
        await client.ConnectAsync();

        var scope = await client.GetExecutionScopeOptionsAsync();
        Console.WriteLine($"scope    : {scope.Groups.Length} group(s), {scope.Projects.Length} project(s)");

        return args[0] switch
        {
            "preflight" => await PreflightAsync(client, manifestPath, modelPath, options),
            "process" => await ProcessAsync(client, manifestPath, modelPath, options),
            "dedup" => await DedupAsync(client, manifestPath, options),
            "diagnostics" => await DiagnosticsAsync(client, options),
            _ => Usage()
        };
    }

    #endregion

    #region Commands

    private static async Task<int> PreflightAsync(WitCloudClient client, string manifestPath, string? modelPath, RunOptions options)
    {
        var parameters = await ParametersAsync(client, manifestPath, modelPath, options);
        var handle = await client.Scripts.SubmitAsync(PREFLIGHT_SCRIPT, parameters);
        Console.WriteLine($"{PREFLIGHT_SCRIPT} submitted: job {handle.JobId}");

        var result = await handle.WaitAsync<CorpusPreflightData>(RESULT, pollInterval: POLL_INTERVAL);
        if (result.Status != ProcessingJobStatus.Completed || result.Result == null)
        {
            Console.WriteLine($"  {result.Status} after {result.Duration}: {result.ErrorMessage}");
            return 1;
        }

        var report = result.Result;
        Console.WriteLine($"  completed in {result.Duration}");
        Console.WriteLine($"  well-formed : {report.WellFormed}");
        Console.WriteLine($"  tasks       : {report.TaskCount}");
        Console.WriteLine($"  traffic     : {report.EstimatedTrafficBytes / BYTES_PER_MIB:F1} MB estimated");
        Console.WriteLine($"  storage     : {report.EstimatedStorageBytes / BYTES_PER_MIB:F1} MB estimated on the server" +
                          (report.StorageHeadroomBytes > 0 ? $", headroom {report.StorageHeadroomBytes / BYTES_PER_MIB:F1} MB" : ", no headroom stated") +
                          (report.StorageRetentionDays > 0 ? $", kept {report.StorageRetentionDays} day(s)" : ""));
        Console.WriteLine($"  refused     : {(report.RefusedOrigins.Count == 0 ? "-" : string.Join(", ", report.RefusedOrigins))}");
        Console.WriteLine($"  oversized   : {report.OversizedSources}");
        foreach (var message in report.Messages)
            Console.WriteLine($"  - {message}");

        return report.WellFormed ? 0 : 1;
    }

    private static async Task<int> ProcessAsync(WitCloudClient client, string manifestPath, string? modelPath, RunOptions options)
    {
        Console.WriteLine($"options  : sampling {options.Sampling:0.##}, max shard {(options.MaxShardMb > 0 ? $"{options.MaxShardMb} MB" : "default")}, runs {options.Runs}");

        var identities = new List<string>();
        for (var i = 1; i <= options.Runs; i++)
        {
            var parameters = await ParametersAsync(client, manifestPath, modelPath, options);
            var handle = await client.Scripts.SubmitAsync(PROCESS_SCRIPT, parameters);
            Console.WriteLine($"[{i}/{options.Runs}] {PROCESS_SCRIPT} submitted: job {handle.JobId}");

            var started = DateTime.UtcNow;
            var report = await handle.WaitAsync<Guid?>(REPORT, pollInterval: POLL_INTERVAL);
            if (report.Status != ProcessingJobStatus.Completed || report.Result == null)
            {
                Console.WriteLine($"  {report.Status} after {report.Duration}: {report.ErrorMessage}");
                return 1;
            }

            Console.WriteLine($"  completed in {report.Duration} (wall {DateTime.UtcNow - started:hh\\:mm\\:ss})");

            var reportBytes = await client.Blobs.DownloadBlobAsync(report.Result.Value);
            identities.Add(PrintReport(reportBytes));
            await SaveAsync(options, $"{REPORT}-{i}.json", reportBytes);

            var integrity = await handle.WaitAsync<Guid?>(INTEGRITY, pollInterval: POLL_INTERVAL);
            if (integrity.Result != null)
            {
                var integrityBytes = await client.Blobs.DownloadBlobAsync(integrity.Result.Value);
                PrintIntegrity(integrityBytes);
                await SaveAsync(options, $"{INTEGRITY}-{i}.json", integrityBytes);
            }
        }

        if (identities.Count < 2)
            return 0;

        var identical = identities.All(identity => identity == identities[0]);
        Console.WriteLine(identical
            ? $"content  : all {identities.Count} run(s) give the same content hashes"
            : $"content  : DIFFER across {identities.Count} run(s)");
        return identical ? 0 : 1;
    }

    private static async Task<int> DedupAsync(WitCloudClient client, string reportPath, RunOptions options)
    {
        var reportBlob = await client.Blobs.UploadBlobAsync(await File.ReadAllBytesAsync(reportPath), Path.GetFileName(reportPath));
        var handle = await client.Scripts.SubmitAsync(DEDUP_SCRIPT, [reportBlob, new CorpusOptionsData { DedupThreshold = options.Threshold }]);
        Console.WriteLine($"{DEDUP_SCRIPT} submitted: job {handle.JobId}");

        var result = await handle.WaitAsync<Guid?>(DEDUP, pollInterval: POLL_INTERVAL);
        if (result.Status != ProcessingJobStatus.Completed || result.Result == null)
        {
            Console.WriteLine($"  {result.Status} after {result.Duration}: {result.ErrorMessage}");
            return 1;
        }

        Console.WriteLine($"  completed in {result.Duration}");
        var bytes = await client.Blobs.DownloadBlobAsync(result.Result.Value);
        await SaveAsync(options, $"{DEDUP}.json", bytes);

        using var document = JsonDocument.Parse(bytes);
        var summary = document.RootElement.GetProperty("summary");
        Console.WriteLine($"  dedup    : {summary.GetProperty("documents").GetInt64()} documents in {summary.GetProperty("shards").GetInt32()} shard(s), " +
                          $"{summary.GetProperty("duplicates").GetInt64()} near-duplicates in {summary.GetProperty("clusters").GetInt64()} cluster(s) at threshold {summary.GetProperty("threshold").GetDouble():0.##}, kept {summary.GetProperty("kept").GetInt64()}");
        foreach (var shard in document.RootElement.GetProperty("shards").EnumerateArray())
            Console.WriteLine($"    [{shard.GetProperty("index").GetInt32(),3}] {shard.GetProperty("documents").GetInt64()} docs, {shard.GetProperty("duplicates").GetInt64()} dropped");

        return 0;
    }

    private static async Task<int> DiagnosticsAsync(WitCloudClient client, RunOptions options)
    {
        var handle = await client.Scripts.RunAsync(DIAGNOSTICS_SCRIPT);
        Console.WriteLine($"{DIAGNOSTICS_SCRIPT} submitted: job {handle.JobId}");

        var result = await handle.WaitAsync<CorpusRuntimeDiagnosticsData>(RESULT, pollInterval: POLL_INTERVAL);
        if (result.Status != ProcessingJobStatus.Completed || result.Result == null)
        {
            Console.WriteLine($"  {result.Status} after {result.Duration}: {result.ErrorMessage}");
            return 1;
        }

        var d = result.Result;
        Console.WriteLine($"  completed in {result.Duration}: controller {d.ControllerVersion}, target {d.RuntimeTarget}");
        Console.WriteLine($"  stages      : {string.Join(" > ", d.Stages)}; fingerprint {d.StageSetSha256[..Math.Min(12, d.StageSetSha256.Length)]}… {(d.StagesVerified ? "verified" : "DIFFERS FROM THE PIN")}");
        Console.WriteLine($"  parsers     : {d.HtmlParser}; {d.Compressor}");
        Console.WriteLine($"  language    : {d.LanguageModelFormat}, reference model {d.ReferenceLanguageModelSha256[..Math.Min(12, d.ReferenceLanguageModelSha256.Length)]}…");
        Console.WriteLine($"  origins     : {string.Join(", ", d.PublicOrigins)}" + (d.MachineOrigins.Count > 0 ? $"; the server's operator lists {string.Join(", ", d.MachineOrigins)}" : ""));
        Console.WriteLine($"  caps        : shard {d.MaxShardBytes / BYTES_PER_MIB:F0} MB, {d.DownloadsPerNode} download(s) per node, {d.MemoryPerShardBytes / BYTES_PER_MIB:F0} MB per shard in flight");

        var capabilities = (await handle.WaitAsync<Guid?>(CAPABILITIES, pollInterval: POLL_INTERVAL)).Result;
        if (capabilities == null)
        {
            Console.WriteLine("  capabilities: (none)");
            return d.StagesVerified ? 0 : 1;
        }

        var bytes = await client.Blobs.DownloadBlobAsync(capabilities.Value);
        await SaveAsync(options, "capabilities.json", bytes);

        using var document = JsonDocument.Parse(bytes);
        var storage = document.RootElement.GetProperty("storage");
        Console.WriteLine($"  capabilities: {document.RootElement.GetProperty("schema").GetString()}, {bytes.Length} bytes; storage headroom " +
                          (storage.TryGetProperty("headroom_bytes", out var headroom) ? $"{headroom.GetInt64() / BYTES_PER_MIB:F1} MB" : "not stated") +
                          (storage.TryGetProperty("retention_days", out var days) ? $", kept {days.GetInt32()} day(s)" : ""));
        if (document.RootElement.TryGetProperty("pool", out var pool))
            Console.WriteLine($"  pool        : {pool.GetProperty("nodes").GetInt32()} node(s), {pool.GetProperty("measured").GetInt32()} measured, {pool.GetProperty("rate").GetDouble():F2} MB/s of WET" +
                              (pool.TryGetProperty("workers", out var workers) ? $", {workers.GetInt32()} shard(s) at once" : ""));

        return d.StagesVerified ? 0 : 1;
    }

    #endregion

    #region Tools

    private static async Task<List<object?>> ParametersAsync(WitCloudClient client, string manifestPath, string? modelPath, RunOptions options)
    {
        var parameters = new List<object?>
        {
            await client.Blobs.UploadBlobAsync(await File.ReadAllBytesAsync(manifestPath), Path.GetFileName(manifestPath)),
            modelPath == null ? Guid.Empty : await client.Blobs.UploadBlobAsync(await File.ReadAllBytesAsync(modelPath), Path.GetFileName(modelPath)),
            new CorpusOptionsData { SamplingRate = options.Sampling, MaxShardBytes = options.MaxShardMb > 0 ? options.MaxShardMb * 1024L * 1024L : 0 }
        };

        return parameters;
    }

    /// <summary>Prints the report and returns the run's identity: the content hashes by shard index.</summary>
    private static string PrintReport(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var summary = root.GetProperty("summary");

        Console.WriteLine($"  report   : {summary.GetProperty("tasks").GetInt32()} shard(s); " +
                          string.Join(", ", summary.GetProperty("verdicts").EnumerateObject().Select(v => $"{v.Name} {v.Value.GetInt32()}")));
        Console.WriteLine($"             records {summary.GetProperty("records_in").GetInt64()}, extracted {summary.GetProperty("documents_extracted").GetInt64()}, out {summary.GetProperty("documents_out").GetInt64()}; " +
                          $"fetched {summary.GetProperty("bytes_fetched").GetInt64() / (1024.0 * 1024.0):F1} MB, produced {summary.GetProperty("bytes_produced").GetInt64() / (1024.0 * 1024.0):F1} MB");
        Console.WriteLine($"             languages: {string.Join(", ", summary.GetProperty("languages").EnumerateObject().OrderByDescending(l => l.Value.GetInt64()).Take(8).Select(l => $"{l.Name} {l.Value.GetInt64()}"))}");
        Console.WriteLine($"             drops: {string.Join(", ", summary.GetProperty("drops").EnumerateObject().OrderByDescending(d => d.Value.GetInt64()).Take(6).Select(d => $"{d.Name} {d.Value.GetInt64()}"))}");

        var hashes = new List<string>();
        foreach (var shard in root.GetProperty("shards").EnumerateArray())
        {
            var stats = shard.GetProperty("stats");
            var line = stats.ValueKind == JsonValueKind.Null
                ? shard.GetProperty("error").GetString()
                : $"{stats.GetProperty("documents_out").GetInt32()} docs, {stats.GetProperty("bytes_fetched").GetInt64() / (1024.0 * 1024.0):F1} MB, fetch {stats.GetProperty("fetch_ms").GetInt32() / 1000.0:F1} s, process {stats.GetProperty("process_ms").GetInt32() / 1000.0:F1} s" +
                  (stats.TryGetProperty("wait_ms", out var wait) ? $", wait {wait.GetInt32() / 1000.0:F1} s" : "");
            Console.WriteLine($"    [{shard.GetProperty("index").GetInt32(),3}] {shard.GetProperty("verdict").GetString(),-19} {line}");
            hashes.Add($"{shard.GetProperty("index").GetInt32()}:{shard.GetProperty("content_sha256").GetString()}");
        }

        return string.Join("\n", hashes);
    }

    private static void PrintIntegrity(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var summary = document.RootElement.GetProperty("summary");
        Console.WriteLine($"  integrity: sampled {summary.GetProperty("sampled").GetInt32()}, compared {summary.GetProperty("compared").GetInt32()}, " +
                          $"version skew {summary.GetProperty("version_skew").GetInt32()}, " +
                          $"incomplete {(summary.TryGetProperty("incomplete", out var incomplete) ? incomplete.GetInt32() : 0)}, mismatches {summary.GetProperty("mismatches").GetInt32()}");

        foreach (var pair in document.RootElement.GetProperty("pairs").EnumerateArray())
        {
            var outcome = pair.TryGetProperty("incomplete", out var unfinished) && unfinished.GetBoolean()
                ? $"incomplete (a side did not finish: {pair.GetProperty("primary").GetProperty("verdict").GetString()} / {pair.GetProperty("replica").GetProperty("verdict").GetString()})"
                : !pair.GetProperty("compared").GetBoolean()
                    ? "version skew"
                    : pair.GetProperty("match").GetBoolean() ? "match" : $"MISMATCH ({string.Join(",", pair.GetProperty("differs").EnumerateArray().Select(d => d.GetString()))})";
            Console.WriteLine($"    [{pair.GetProperty("index").GetInt32(),3}] {outcome}");
        }
    }

    private static async Task SaveAsync(RunOptions options, string name, byte[] bytes)
    {
        if (options.SaveDir == null)
            return;

        Directory.CreateDirectory(options.SaveDir);
        await File.WriteAllBytesAsync(Path.Combine(options.SaveDir, name), bytes);
    }

    private static string? OptionValue(string[] args, string name, string? fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    private static int OptionValue(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
    }

    private static double OptionValue(string[] args, string name, double fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length
               && double.TryParse(args[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static int Usage()
    {
        Console.WriteLine("corpus-live preflight <manifest.json> [--model <model.bin>]");
        Console.WriteLine("corpus-live process   <manifest.json> [--model <model.bin>] [--sampling R] [--max-shard-mb N] [--runs N] [--save <dir>]");
        Console.WriteLine("corpus-live dedup     <report.json> [--threshold J] [--save <dir>]");
        Console.WriteLine("corpus-live diagnostics [--save <dir>]");
        Console.WriteLine();
        Console.WriteLine($"env: {KEY_ENV} (wit_sk_ key, required), {SERVER_ENV} (default {DEFAULT_SERVER}), {IDENTITY_ENV} (default {DEFAULT_IDENTITY})");
        return 2;
    }

    #endregion

    #region Types

    private sealed record RunOptions(int Runs, double Sampling, int MaxShardMb, string? SaveDir, double Threshold);

    #endregion
}
