using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Live;

/// <summary>
/// Drives the Verify scripts on a real server through the public SDK, as an initiator would:
/// <c>diagnostics</c> runs <c>VerifyRuntimeDiagnostics</c> (the server's module); <c>run</c>,
/// <c>generate</c> and <c>check</c> upload their documents, run <c>VerifyRun</c>,
/// <c>VerifyGenerate</c> or <c>VerifyGenerateAndCheck</c>, download every output blob, print
/// the report summary and the provenance (which node ran what), and over repeated runs check
/// that the report and the corpus are byte-identical.
/// </summary>
public static class Program
{
    #region Constants

    private const string SERVER_ENV = "WITAI_VERIFY_LIVE_SERVER";
    private const string IDENTITY_ENV = "WITAI_VERIFY_LIVE_IDENTITY";
    private const string KEY_ENV = "WITAI_VERIFY_LIVE_KEY";

    private const string DEFAULT_SERVER = "https://test.omnibuscloud.com";
    private const string DEFAULT_IDENTITY = "https://auth.omnibuscloud.com";

    private const string DIAGNOSTICS_SCRIPT = "VerifyRuntimeDiagnostics";
    private const string RUN_SCRIPT = "VerifyRun";
    private const string GENERATE_SCRIPT = "VerifyGenerate";
    private const string CHECK_SCRIPT = "VerifyGenerateAndCheck";

    private const string REPORT = "report";
    private const string CORPUS = "corpus";
    private const string PROVENANCE = "provenance";
    private const string INTEGRITY = "integrity";

    private const int DEFAULT_RUNS = 1;
    private const int DEFAULT_BATCH_SIZE = 8;

    private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(2);

    #endregion

    #region Entry

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
            return Usage();

        var key = Environment.GetEnvironmentVariable(KEY_ENV);
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine($"{KEY_ENV} is not set (a wit_sk_ application key).");
            return 2;
        }

        var server = Environment.GetEnvironmentVariable(SERVER_ENV) ?? DEFAULT_SERVER;
        var identity = Environment.GetEnvironmentVariable(IDENTITY_ENV) ?? DEFAULT_IDENTITY;
        var options = new RunOptions(
            OptionValue(args, "--runs", DEFAULT_RUNS),
            OptionValue(args, "--batch-size", DEFAULT_BATCH_SIZE),
            OptionValue(args, "--max-degree", 0),
            OptionValue(args, "--sampling", 0.0),
            OptionValue(args, "--save", null));

        Console.WriteLine($"server   : {server}");
        Console.WriteLine($"identity : {identity}");

        await using var client = new WitCloudClient(server, identity, key);
        await client.ConnectAsync();

        var scope = await client.GetExecutionScopeOptionsAsync();
        Console.WriteLine($"scope    : {scope.Groups.Length} group(s), {scope.Projects.Length} project(s)");

        return args[0] switch
        {
            "diagnostics" => await DiagnosticsAsync(client, options.Runs),
            "run" when args.Length >= 2 => await ScriptAsync(client, RUN_SCRIPT, [args[1]], [REPORT, PROVENANCE, INTEGRITY], options),
            "generate" when args.Length >= 2 => await ScriptAsync(client, GENERATE_SCRIPT, [args[1]], [CORPUS, REPORT, PROVENANCE, INTEGRITY], options),
            "check" when args.Length >= 3 => await ScriptAsync(client, CHECK_SCRIPT, [args[1], args[2]], [REPORT, CORPUS, PROVENANCE, INTEGRITY], options),
            _ => Usage()
        };
    }

    #endregion

    #region Commands

    private static async Task<int> DiagnosticsAsync(WitCloudClient client, int runs)
    {
        var failures = 0;
        for (var i = 1; i <= runs; i++)
        {
            var handle = await client.Scripts.RunAsync(DIAGNOSTICS_SCRIPT);
            Console.WriteLine($"[{i}/{runs}] {DIAGNOSTICS_SCRIPT} submitted: job {handle.JobId}");

            var result = await handle.WaitAsync<VerifyRuntimeDiagnosticsData>();
            if (result.Status != ProcessingJobStatus.Completed || result.Result == null)
            {
                failures++;
                Console.WriteLine($"  {result.Status} after {result.Duration}: {result.ErrorMessage}");
                continue;
            }

            var d = result.Result;
            Console.WriteLine($"  completed in {result.Duration}: target={d.RuntimeTarget} wasmtime={d.WasmtimeVersion} root={(d.RuntimesRootFound ? "found" : "MISSING")}");
            foreach (var runtime in d.Runtimes)
            {
                var pin = runtime.Sha256.Length >= 12 ? $"sha256 {runtime.Sha256[..12]}…" : "(task-supplied module)";
                Console.WriteLine($"    {runtime.RuntimeId,-28} {(runtime.Available ? "available" : "MISSING")}  {pin}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> ScriptAsync(WitCloudClient client, string script, IReadOnlyList<string> inputPaths, IReadOnlyList<string> outputs, RunOptions options)
    {
        var inputs = new List<(string Name, byte[] Bytes)>();
        foreach (var path in inputPaths)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"input not found: {path}");
                return 2;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Console.WriteLine($"input    : {Path.GetFileName(path)}, {lines} line(s), {bytes.Length} byte(s)");
            inputs.Add((Path.GetFileName(path), bytes));
        }

        Console.WriteLine($"options  : batch size {options.BatchSize}, max degree {(options.MaxDegree > 0 ? options.MaxDegree.ToString() : "default")}, sampling {options.Sampling:0.##}");

        var identity = outputs.Where(name => name is REPORT or CORPUS).ToDictionary(name => name, _ => new List<byte[]>());

        for (var i = 1; i <= options.Runs; i++)
        {
            var parameters = new List<object?>();
            foreach (var (name, bytes) in inputs)
                parameters.Add(await client.Blobs.UploadBlobAsync(bytes, name));
            parameters.Add(new VerifyOptionsData { BatchSize = options.BatchSize, MaxDegree = options.MaxDegree, SamplingRate = options.Sampling });

            var handle = await client.Scripts.SubmitAsync(script, parameters);
            Console.WriteLine($"[{i}/{options.Runs}] {script} submitted: job {handle.JobId}");

            var first = await handle.WaitAsync<Guid?>(outputs[0], pollInterval: POLL_INTERVAL);
            if (first.Status != ProcessingJobStatus.Completed)
            {
                Console.WriteLine($"  {first.Status} after {first.Duration}: {first.ErrorMessage}");
                return 1;
            }

            Console.WriteLine($"  completed in {first.Duration}");

            foreach (var name in outputs)
            {
                Guid? blobId;
                if (name == outputs[0])
                {
                    blobId = first.Result;
                }
                else
                {
                    // An output the server's script version does not produce (an older Scripts package) is reported, not fatal.
                    try
                    {
                        blobId = (await handle.WaitAsync<Guid?>(name, pollInterval: POLL_INTERVAL)).Result;
                    }
                    catch (InvalidOperationException exception) when (exception.InnerException?.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        Console.WriteLine($"  {name}: (not an output of the server's script version)");
                        continue;
                    }
                }

                if (blobId == null)
                {
                    Console.WriteLine($"  {name}: (none)");
                    continue;
                }

                var bytes = await client.Blobs.DownloadBlobAsync(blobId.Value);
                if (identity.TryGetValue(name, out var history))
                    history.Add(bytes);

                if (options.SaveDir != null)
                {
                    Directory.CreateDirectory(options.SaveDir);
                    var extension = name == CORPUS ? "jsonl" : "json";
                    var savedPath = Path.Combine(options.SaveDir, $"{name}-{i}.{extension}");
                    await File.WriteAllBytesAsync(savedPath, bytes);
                }

                Print(name, bytes);
            }
        }

        var verdict = 0;
        foreach (var (name, history) in identity)
        {
            if (history.Count < 2)
                continue;

            var identical = history.All(bytes => bytes.AsSpan().SequenceEqual(history[0]));
            Console.WriteLine(identical
                ? $"{name,-9}: all {history.Count} byte-identical"
                : $"{name,-9}: DIFFER across {history.Count} run(s)");
            if (!identical)
                verdict = 1;
        }

        return verdict;
    }

    #endregion

    #region Tools

    private static void Print(string name, byte[] bytes)
    {
        switch (name)
        {
            case REPORT:
                Console.WriteLine($"  report: {bytes.Length} byte(s), sha256 {Sha256(bytes)[..16]}…");
                PrintSummary(bytes);
                break;
            case CORPUS:
                var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                Console.WriteLine($"  corpus: {lines} line(s), {bytes.Length} byte(s), sha256 {Sha256(bytes)[..16]}…");
                break;
            case PROVENANCE:
                PrintProvenance(bytes);
                break;
            case INTEGRITY:
                PrintIntegrity(bytes);
                break;
            default:
                Console.WriteLine($"  {name}: {bytes.Length} byte(s)");
                break;
        }
    }

    private static void PrintSummary(byte[] report)
    {
        using var document = JsonDocument.Parse(report);
        var summary = document.RootElement.GetProperty("summary");
        Console.WriteLine($"    summary: total={summary.GetProperty("total").GetInt32()} pass={summary.GetProperty("pass").GetInt32()} pass_rate={summary.GetProperty("pass_rate").GetDouble():F3}");

        var verdicts = summary.GetProperty("verdicts");
        var histogram = string.Join(", ", verdicts.EnumerateObject().Where(p => p.Value.GetInt32() > 0).Select(p => $"{p.Name}={p.Value.GetInt32()}"));
        Console.WriteLine($"    verdicts: {histogram}");
    }

    private static void PrintProvenance(byte[] provenance)
    {
        using var document = JsonDocument.Parse(provenance);
        var nodes = document.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        Console.WriteLine($"  provenance: {nodes.Count} node(s)");
        foreach (var node in nodes)
        {
            Console.WriteLine($"    {node.GetProperty("node_name").GetString(),-14} {node.GetProperty("runtime_target").GetString(),-11} wasmtime {node.GetProperty("wasmtime").GetString()}  controller {node.GetProperty("controller").GetString()}  batches {node.GetProperty("batches").GetInt32()}  tasks {node.GetProperty("tasks").GetInt32()}  native {node.GetProperty("native_library").GetString()}");
        }

        foreach (var batch in document.RootElement.GetProperty("batches").EnumerateArray())
        {
            var tasks = batch.GetProperty("tasks").EnumerateArray().Select(t => t.GetInt32()).ToList();
            var range = tasks.Count == 0 ? "[]" : tasks.Count == 1 ? $"[{tasks[0]}]" : $"[{tasks[0]}..{tasks[^1]}] ({tasks.Count})";
            var nodeName = batch.TryGetProperty("node_name", out var n) ? n.GetString() : "?";
            var runtime = batch.TryGetProperty("runtime", out var r) ? r.GetString() : "?";
            var degree = batch.TryGetProperty("degree", out var d) ? d.GetInt32() : 0;
            var wall = batch.TryGetProperty("wall_ms", out var w) ? w.GetInt32() : 0;
            var fuel = batch.TryGetProperty("fuel", out var f) ? f.GetInt64() : 0;
            Console.WriteLine($"    batch {nodeName,-14} {runtime,-16} degree {degree,2}  tasks {range}  {wall} ms  fuel {fuel:N0}");
        }
    }

    private static void PrintIntegrity(byte[] integrity)
    {
        using var document = JsonDocument.Parse(integrity);
        var summary = document.RootElement.GetProperty("summary");
        var sampled = summary.GetProperty("sampled").GetInt32();
        if (sampled == 0)
        {
            Console.WriteLine("  integrity: no sampling");
            return;
        }

        Console.WriteLine($"  integrity: sampled {sampled}, compared {summary.GetProperty("compared").GetInt32()}, same node {summary.GetProperty("same_node").GetInt32()}, unpaired {summary.GetProperty("unpaired").GetInt32()}, mismatches {summary.GetProperty("mismatches").GetInt32()} (rate {summary.GetProperty("mismatch_rate").GetDouble():0.###})");
        foreach (var pair in document.RootElement.GetProperty("pairs").EnumerateArray())
        {
            if (!pair.GetProperty("compared").GetBoolean() || pair.GetProperty("match").GetBoolean())
                continue;

            var primary = pair.GetProperty("primary");
            var replica = pair.GetProperty("replica");
            var differs = string.Join(",", pair.GetProperty("differs").EnumerateArray().Select(f => f.GetString()));
            Console.WriteLine($"    MISMATCH task {pair.GetProperty("index").GetInt32()}: {primary.GetProperty("node_name").GetString()} {primary.GetProperty("verdict").GetString()} vs {replica.GetProperty("node_name").GetString()} {replica.GetProperty("verdict").GetString()} ({differs})");
        }
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
               && double.TryParse(args[index + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static int Usage()
    {
        Console.WriteLine("verify-live diagnostics [--runs N]");
        Console.WriteLine("verify-live run      <taskset.jsonl>                  [--runs N] [--batch-size B] [--max-degree D] [--sampling R] [--save DIR]");
        Console.WriteLine("verify-live generate <generators.jsonl>               [--runs N] [--batch-size B] [--max-degree D] [--sampling R] [--save DIR]");
        Console.WriteLine("verify-live check    <generators.jsonl> <checker.json> [--runs N] [--batch-size B] [--max-degree D] [--sampling R] [--save DIR]");
        Console.WriteLine();
        Console.WriteLine($"  {SERVER_ENV}    server URL (default {DEFAULT_SERVER})");
        Console.WriteLine($"  {IDENTITY_ENV}  identity URL (default {DEFAULT_IDENTITY})");
        Console.WriteLine($"  {KEY_ENV}       a wit_sk_ application key (required)");
        return 2;
    }

    #endregion

    #region Types

    private sealed record RunOptions(int Runs, int BatchSize, int MaxDegree, double Sampling, string? SaveDir);

    #endregion
}
