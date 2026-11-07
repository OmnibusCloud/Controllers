using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutWit.Cloud.Data.Processing;
using OutWit.Cloud.SDK;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Live;

/// <summary>
/// Drives the Verify scripts on a real server through the public SDK, as an initiator would:
/// <c>diagnostics</c> runs <c>VerifyRuntimeDiagnostics</c> (one node per run, so repeated runs
/// sample the fleet); <c>run</c> uploads a taskset, runs <c>VerifyRun</c>, downloads the verdict
/// report and, over repeated runs, checks that the reports are byte-identical.
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

    private const int DEFAULT_RUNS = 1;
    private const int DEFAULT_BATCH_SIZE = 8;

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
        var runs = OptionValue(args, "--runs", DEFAULT_RUNS);

        Console.WriteLine($"server   : {server}");
        Console.WriteLine($"identity : {identity}");

        await using var client = new WitCloudClient(server, identity, key);
        await client.ConnectAsync();

        var scope = await client.GetExecutionScopeOptionsAsync();
        Console.WriteLine($"scope    : {scope.Groups.Length} group(s), {scope.Projects.Length} project(s)");

        return args[0] switch
        {
            "diagnostics" => await DiagnosticsAsync(client, runs),
            "run" when args.Length >= 2 => await RunAsync(client, args[1], runs, OptionValue(args, "--batch-size", DEFAULT_BATCH_SIZE), OptionValue(args, "--save", null)),
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
                Console.WriteLine($"    {runtime.RuntimeId,-16} {(runtime.Available ? "available" : "MISSING")}  sha256 {runtime.Sha256[..12]}…");
        }

        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> RunAsync(WitCloudClient client, string tasksetPath, int runs, int batchSize, string? saveDir)
    {
        if (!File.Exists(tasksetPath))
        {
            Console.Error.WriteLine($"taskset not found: {tasksetPath}");
            return 2;
        }

        var taskset = await File.ReadAllBytesAsync(tasksetPath);
        var lines = Encoding.UTF8.GetString(taskset).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Console.WriteLine($"taskset  : {Path.GetFileName(tasksetPath)}, {lines} line(s), {taskset.Length} byte(s), batch size {batchSize}");

        var reports = new List<byte[]>();
        for (var i = 1; i <= runs; i++)
        {
            var blobId = await client.Blobs.UploadBlobAsync(taskset, Path.GetFileName(tasksetPath));
            var options = new VerifyOptionsData { BatchSize = batchSize };

            var handle = await client.Scripts.SubmitAsync(RUN_SCRIPT, [blobId, options]);
            Console.WriteLine($"[{i}/{runs}] {RUN_SCRIPT} submitted: job {handle.JobId} (taskset blob {blobId})");

            var result = await handle.WaitAsync<Guid?>("report", pollInterval: TimeSpan.FromSeconds(2));
            if (result.Status != ProcessingJobStatus.Completed || result.Result == null)
            {
                Console.WriteLine($"  {result.Status} after {result.Duration}: {result.ErrorMessage}");
                return 1;
            }

            var report = await client.Blobs.DownloadBlobAsync(result.Result.Value);
            reports.Add(report);
            if (saveDir != null)
            {
                Directory.CreateDirectory(saveDir);
                var savedPath = Path.Combine(saveDir, $"report-{i}.json");
                await File.WriteAllBytesAsync(savedPath, report);
                Console.WriteLine($"  saved: {savedPath}");
            }
            Console.WriteLine($"  completed in {result.Duration}: report {report.Length} byte(s), sha256 {Sha256(report)[..16]}…");
            PrintSummary(report);
        }

        if (reports.Count < 2)
            return 0;

        var identical = reports.All(r => r.AsSpan().SequenceEqual(reports[0]));
        Console.WriteLine(identical
            ? $"reports  : all {reports.Count} byte-identical"
            : $"reports  : DIFFER across {reports.Count} run(s)");
        return identical ? 0 : 1;
    }

    #endregion

    #region Tools

    private static void PrintSummary(byte[] report)
    {
        using var document = JsonDocument.Parse(report);
        var summary = document.RootElement.GetProperty("summary");
        Console.WriteLine($"  summary: total={summary.GetProperty("total").GetInt32()} pass={summary.GetProperty("pass").GetInt32()} pass_rate={summary.GetProperty("pass_rate").GetDouble():F3} total_fuel={summary.GetProperty("total_fuel").GetInt64()}");

        var verdicts = summary.GetProperty("verdicts");
        var histogram = string.Join(", ", verdicts.EnumerateObject().Select(p => $"{p.Name}={p.Value.GetInt32()}"));
        Console.WriteLine($"  verdicts: {histogram}");
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

    private static int Usage()
    {
        Console.WriteLine("verify-live diagnostics [--runs N]");
        Console.WriteLine("verify-live run <taskset.jsonl> [--runs N] [--batch-size B] [--save DIR]");
        Console.WriteLine();
        Console.WriteLine($"  {SERVER_ENV}    server URL (default {DEFAULT_SERVER})");
        Console.WriteLine($"  {IDENTITY_ENV}  identity URL (default {DEFAULT_IDENTITY})");
        Console.WriteLine($"  {KEY_ENV}       a wit_sk_ application key (required)");
        return 2;
    }

    #endregion
}
