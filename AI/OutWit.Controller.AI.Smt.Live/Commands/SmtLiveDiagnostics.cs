using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Server;
using OutWit.Controller.AI.Smt.Model;
using static OutWit.Controller.AI.Smt.Live.Documents.SmtLiveJson;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>diagnostics</c>: runs <c>SmtRuntimeDiagnostics</c> and prints what the controller of the
/// server's process carries and can do, then the capabilities document, held to its schema (and
/// saved when a folder is named).
/// </summary>
internal sealed class SmtLiveDiagnostics
{
    #region Constants

    private const string SCRIPT = "SmtRuntimeDiagnostics";
    private const string RESULT = "result";
    private const double BYTES_PER_MIB = 1024.0 * 1024.0;
    private const double BYTES_PER_GIB = BYTES_PER_MIB * 1024.0;

    #endregion

    #region Fields

    private readonly ISmtLiveServer m_server;
    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes the command.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="output">Where the lines go.</param>
    public SmtLiveDiagnostics(ISmtLiveServer server, TextWriter output)
    {
        m_server = server;
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs the diagnostics.
    /// </summary>
    /// <param name="folder">Where the capabilities document is saved; null keeps it nowhere.</param>
    /// <param name="ct">Stops the local waiting.</param>
    /// <returns>The exit code: 0 when the job completed and the capabilities hold to their schema.</returns>
    public async Task<int> RunAsync(string? folder, CancellationToken ct)
    {
        var schema = SmtLiveDocuments.SchemaOf(SmtLiveDocuments.CAPABILITIES);

        var job = await m_server.SubmitAsync(SCRIPT, [], ct);
        m_output.WriteLine($"{SCRIPT} submitted: job {job.Id}");

        SmtLiveJobEnd end;
        try
        {
            end = await job.WaitAsync(new SmtLiveProgress(m_output, "progress"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}stopped (Ctrl+C) while waiting for the job {job.Id}: it goes on on the server, where it runs on the server alone");
            return SmtLiveExitCodes.STOPPED;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}the job's status could not be read: {exception.Message}; the job {job.Id} goes on on the server, where it runs on the server alone");
            return SmtLiveExitCodes.ERROR;
        }
        if (!end.Completed)
        {
            m_output.WriteLine($"  {end.Status} after {end.Duration:hh\\:mm\\:ss}: {end.Error}");
            return SmtLiveExitCodes.FAILED;
        }

        m_output.WriteLine($"  completed in {end.Duration:hh\\:mm\\:ss}");

        var diagnostics = await job.ResultAsync<SmtRuntimeDiagnosticsData>(RESULT, ct);
        if (diagnostics == null)
        {
            m_output.WriteLine("  no diagnostics came back");
            return SmtLiveExitCodes.FAILED;
        }

        Print(diagnostics);

        var blob = await job.ResultAsync<Guid?>(SmtLiveDocuments.CAPABILITIES.Variable, ct);
        if (blob == null)
        {
            m_output.WriteLine("capabilities: none came back");
            return SmtLiveExitCodes.FAILED;
        }

        var keep = folder != null;
        var path = Path.Combine(Path.GetTempPath(), $"smt-live-capabilities-{Guid.NewGuid():N}.json");
        if (folder != null)
        {
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, SmtLiveDocuments.CAPABILITIES.File);
        }

        try
        {
            await m_server.DownloadAsync(blob.Value, path, null, ct);
            var verdict = SmtLiveDocumentCheck.CheckFile(path, schema, false);
            m_output.WriteLine($"capabilities: {verdict.Bytes} bytes{(keep ? $", saved as {path}" : "")}: {(verdict.IsValid ? $"valid ({schema.Name})" : $"DOES NOT HOLD TO {schema.Name}")}");
            foreach (var error in verdict.Errors)
                m_output.WriteLine($"  {error}");

            PrintCapabilities(path);
            return verdict.IsValid ? SmtLiveExitCodes.OK : SmtLiveExitCodes.FAILED;
        }
        finally
        {
            if (!keep)
                File.Delete(path);
        }
    }

    private void Print(SmtRuntimeDiagnosticsData d)
    {
        m_output.WriteLine($"  controller  : {d.ControllerVersion}, profile {d.ProfileId}, solver {d.SolverVersion}");
        m_output.WriteLine($"  module      : {d.ModuleFile}, {d.ModuleSize} bytes, sha256 {d.ModuleSha256}; " +
                           $"present in the server's process: {(d.ModulePresent ? "yes" : "NO")}; compiled there: {(d.ModuleCompiled ? $"yes, in {d.CompileMs} ms" : "no")}");
        if (d.SandboxProblem.Length > 0)
            m_output.WriteLine($"  sandbox     : {d.SandboxProblem}");

        m_output.WriteLine($"  engine      : wasmtime binding {(d.WasmtimeBinding.Length == 0 ? "NOT LOADED" : d.WasmtimeBinding)}, natives {d.NativesVersion}, " +
                           $"sha256 {(d.NativesSha256.Length == 0 ? "(none for this platform)" : d.NativesSha256)}; pinned: {(d.NativesPinned ? "yes" : "NO")}");
        m_output.WriteLine($"  platform    : {d.Platform}, traps {d.TrapMode}");
        m_output.WriteLine($"  degree      : {d.Degree} session(s) at once ({d.DegreeReason}): {d.LogicalCores} core(s), " +
                           $"{d.AvailableMemoryBytes / BYTES_PER_GIB:F1} GiB available, {d.SessionMemoryBytes / BYTES_PER_MIB:F0} MiB a session under the default options");
    }

    private void PrintCapabilities(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            m_output.WriteLine($"  schema      : {Text(root, "schema")}, controller {Text(root, "controller")}");
            m_output.WriteLine($"  profile     : {Text(root, "profile", "id")}, {Text(root, "profile", "solver")} {Text(root, "profile", "solver_version")}, " +
                               $"module sha256 {Text(root, "profile", "module_sha256")}, present on the server {Text(root, "profile", "module_present")}; engine {Text(root, "profile", "engine")}");
            m_output.WriteLine($"  queries     : {string.Join(", ", Strings(root, "queries"))}");
            m_output.WriteLine($"  defaults    : {Counts(root, "defaults")}");
            m_output.WriteLine($"  ceilings    : {Counts(root, "ceilings")}");
            m_output.WriteLine($"  time        : {Counts(root, "time")}");
            m_output.WriteLine($"  node        : {Counts(root, "node")}");
            m_output.WriteLine($"  platforms   : {string.Join(", ", Strings(root, "platforms"))}; benchmark unit {Text(root, "benchmark_unit")}");

            var pool = At(root, "pool");
            m_output.WriteLine(pool.ValueKind != JsonValueKind.Object
                ? "  pool        : (the host did not say)"
                : $"  pool        : {Number(pool, "nodes")} node(s), {Number(pool, "measured")} measured, " +
                  $"{(At(pool, "workers").ValueKind == JsonValueKind.Number ? $"{Number(pool, "workers")} session(s) at once" : "sessions not carried by the host")}, rate {Text(pool, "rate")}");
        }
        catch (JsonException)
        {
            m_output.WriteLine("  (the capabilities are not JSON)");
        }
    }

    private static IEnumerable<string> Strings(JsonElement root, string name)
    {
        var items = At(root, name);
        return items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Select(item => item.ToString()) : [];
    }

    #endregion
}
