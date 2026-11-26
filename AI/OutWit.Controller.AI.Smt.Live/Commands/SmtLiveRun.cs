using System.Globalization;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Server;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>run</c>: uploads a problem set, runs <c>SmtRun</c> with the options, waits with the job's
/// progress, downloads the four documents (results, report, integrity, provenance) into a
/// folder, holds each to its schema, and prints the report's summary. <c>fetch</c> does the
/// same for a job of <c>SmtRun</c> submitted earlier, by its id. Options out of their ranges
/// and a folder that cannot take the documents are refused before anything is sent. Once the job
/// has ended, its documents are downloaded before anything else is done, each on its own: one
/// that cannot be downloaded leaves the others. Whatever stops the tool after the submit (Ctrl+C,
/// a connection lost for longer than the waiting tolerates, a download that fails) says that the
/// job goes on on the server, and how to fetch its documents. What the run took, end to end from
/// the tool's start, is written beside the documents (<see cref="SmtLiveFleetFacts"/>): the
/// fleet side of the comparative test; a fetch measures nothing.
/// </summary>
internal sealed class SmtLiveRun
{
    #region Constants

    /// <summary>The script a run submits and a fetch takes.</summary>
    public const string SCRIPT = "SmtRun";

    private const string FOLDER_PREFIX = "smt-run-";
    private const int FOLDER_ID_CHARACTERS = 12;

    #endregion

    #region Fields

    private readonly ISmtLiveServer m_server;
    private readonly TextWriter m_output;
    private readonly SmtLiveClock m_clock;

    #endregion

    #region Constructors

    /// <summary>
    /// Makes the command.
    /// </summary>
    /// <param name="server">The server.</param>
    /// <param name="output">Where the lines go.</param>
    /// <param name="clock">The clock the times are read from: the tool's start; null for one that starts now.</param>
    public SmtLiveRun(ISmtLiveServer server, TextWriter output, SmtLiveClock? clock = null)
    {
        m_server = server;
        m_output = output;
        m_clock = clock ?? SmtLiveClock.StartNow();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Refuses what a run cannot be given, before anything is sent: options out of their ranges
    /// (the controller would refuse the run) and a folder that cannot take the documents.
    /// </summary>
    /// <param name="options">The run's options as given.</param>
    /// <param name="folder">The folder of its documents; made here when it is not there.</param>
    /// <param name="output">Where a refusal is said.</param>
    /// <returns>The exit code of a refusal; null when the run may go.</returns>
    public static int? Refuse(SmtOptionsData options, string folder, TextWriter output)
    {
        var refused = SmtOptionsRules.Check(options);
        if (refused.Count > 0)
        {
            output.WriteLine("the options are out of range; nothing was sent:");
            foreach (var problem in refused)
                output.WriteLine($"  {problem}");

            return SmtLiveExitCodes.USAGE;
        }

        if (SmtLiveFolder.Prepare(folder, out var unusable))
            return null;

        output.WriteLine($"{unusable}; nothing was sent");
        return SmtLiveExitCodes.USAGE;
    }

    /// <summary>
    /// The folder a run's documents go to when none is named: <c>smt-run-</c> and the tool's start.
    /// </summary>
    /// <param name="clock">The tool's clock.</param>
    /// <returns>The folder, in the current folder.</returns>
    public static string DefaultFolder(SmtLiveClock clock)
    {
        return FOLDER_PREFIX + clock.StartedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The folder a fetched run's documents go to when none is named: <c>smt-run-</c> and the head of the job's id.
    /// </summary>
    /// <param name="id">The job.</param>
    /// <returns>The folder, in the current folder.</returns>
    public static string FetchFolder(Guid id)
    {
        return FOLDER_PREFIX + id.ToString("N")[..FOLDER_ID_CHARACTERS];
    }

    /// <summary>
    /// Runs a problem set.
    /// </summary>
    /// <param name="path">The problem set.</param>
    /// <param name="options">The run's options as given.</param>
    /// <param name="folder">Where the documents go; null for <see cref="DefaultFolder"/>.</param>
    /// <param name="ct">Stops the tool's part: the upload, the waiting, the downloads; a submitted job goes on on the server.</param>
    /// <returns>
    /// The exit code: 0 when the run completed, every document came and holds to its schema, and
    /// nothing is alarming; 3 when something is (<see cref="SmtLiveRunSummary"/>); 1 when the job
    /// did not complete or a document did not come or does not hold; 2 when the options or the
    /// folder are refused; 5 when Ctrl+C stopped it; 6 when the connection or this machine failed.
    /// </returns>
    public async Task<int> RunAsync(string path, SmtOptionsData options, string? folder, CancellationToken ct)
    {
        var target = folder ?? DefaultFolder(m_clock);
        m_output.WriteLine($"options  : {SmtLiveInput.Describe(options)}");
        if (Refuse(options, target, m_output) is { } refused)
            return refused;

        try
        {
            var connected = m_clock.Elapsed;
            (Guid Blob, int Rows, long Bytes, string Sha256) set;
            ISmtLiveJob job;
            try
            {
                set = await SmtLiveInput.UploadAsync(m_server, path, m_output, ct);
                job = await m_server.SubmitAsync(SCRIPT, [set.Blob, options], ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                m_output.WriteLine($"{SmtLiveRunSummary.LOUD}stopped (Ctrl+C) before the run was submitted: no job of it runs on the server");
                return SmtLiveExitCodes.STOPPED;
            }

            var uploaded = m_clock.Elapsed;
            m_output.WriteLine($"{SCRIPT} submitted: job {job.Id}");
            return await FinishAsync(job, target, (Path.GetFileName(path), set.Rows, set.Bytes, set.Sha256, connected, uploaded), ct);
        }
        finally
        {
            SmtLiveFolder.RemoveIfEmpty(target);
        }
    }

    /// <summary>
    /// Fetches a run by its job: waits while it runs, then downloads, checks and summarises its
    /// documents as <see cref="RunAsync"/> does. Nothing is measured.
    /// </summary>
    /// <param name="id">The job of <c>SmtRun</c>.</param>
    /// <param name="folder">Where the documents go; null for <see cref="FetchFolder"/>.</param>
    /// <param name="ct">Stops the waiting and the downloads; the job goes on on the server.</param>
    /// <returns>The exit code, as <see cref="RunAsync"/> gives it; 2 as well when the job is not one of <c>SmtRun</c>.</returns>
    public async Task<int> FetchAsync(Guid id, string? folder, CancellationToken ct)
    {
        var target = folder ?? FetchFolder(id);
        if (!SmtLiveFolder.Prepare(target, out var unusable))
        {
            m_output.WriteLine($"{unusable}; nothing was fetched");
            return SmtLiveExitCodes.USAGE;
        }

        try
        {
            ISmtLiveJob job;
            try
            {
                job = await m_server.OpenAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                m_output.WriteLine($"{SmtLiveRunSummary.LOUD}stopped (Ctrl+C): nothing was fetched");
                return SmtLiveExitCodes.STOPPED;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                m_output.WriteLine($"{SmtLiveRunSummary.LOUD}the job {id} cannot be read: {exception.Message}");
                return SmtLiveExitCodes.ERROR;
            }

            if (job.Script != SCRIPT)
            {
                m_output.WriteLine($"the job {id} runs {job.Script}, not {SCRIPT}: fetch takes the job of a run; nothing was fetched");
                return SmtLiveExitCodes.USAGE;
            }

            m_output.WriteLine($"{SCRIPT} fetched: job {job.Id}");
            return await FinishAsync(job, target, null, ct);
        }
        finally
        {
            SmtLiveFolder.RemoveIfEmpty(target);
        }
    }

    /// <summary>
    /// Waits for a job to end, downloads its four documents before anything else, holds each to
    /// its schema, writes what the run measured (when it was measured) and prints the summary.
    /// </summary>
    private async Task<int> FinishAsync(ISmtLiveJob job, string folder, (string File, int Rows, long Bytes, string Sha256, TimeSpan Connected, TimeSpan Uploaded)? measured,
        CancellationToken ct)
    {
        var schemas = SmtLiveDocuments.RUN.ToDictionary(document => document, SmtLiveDocuments.SchemaOf);

        SmtLiveJobEnd end;
        try
        {
            end = await job.WaitAsync(new SmtLiveProgress(m_output, "progress"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Left(job, folder, "stopped (Ctrl+C) while waiting for the job");
            return SmtLiveExitCodes.STOPPED;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Left(job, folder, $"the job's status could not be read: {exception.Message}");
            return SmtLiveExitCodes.ERROR;
        }

        if (!end.Completed)
        {
            m_output.WriteLine($"  {end.Status} after {end.Duration:hh\\:mm\\:ss}: {end.Error}");
            return SmtLiveExitCodes.FAILED;
        }

        m_output.WriteLine($"  completed in {end.Duration:hh\\:mm\\:ss}");
        var ended = m_clock.Elapsed;
        m_output.WriteLine($"documents: {Path.GetFullPath(folder)}");

        // The documents first: nothing that can fail is done before every one of them was asked for.
        var verdicts = new Dictionary<SmtLiveDocument, SmtLiveDocumentVerdict?>();
        var undownloaded = new List<string>();
        foreach (var document in SmtLiveDocuments.RUN)
        {
            try
            {
                verdicts[document] = await FetchAsync(job, document, schemas[document], folder, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Left(job, folder, "stopped (Ctrl+C) while its documents were downloaded");
                return SmtLiveExitCodes.STOPPED;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                m_output.WriteLine($"{SmtLiveRunSummary.LOUD}{document.File,-16} COULD NOT BE DOWNLOADED: {exception.Message}");
                undownloaded.Add(document.File);
            }
        }

        var wall = m_clock.Elapsed;
        if (measured is { } set && verdicts.GetValueOrDefault(SmtLiveDocuments.REPORT) != null)
            WriteFacts(job, end, folder, set, ended, wall);

        if (undownloaded.Count > 0)
        {
            Left(job, folder, $"{string.Join(", ", undownloaded)} could not be downloaded");
            return SmtLiveExitCodes.ERROR;
        }

        var missing = SmtLiveDocuments.RUN.Where(document => verdicts[document] == null).Select(document => document.File).ToList();
        if (verdicts[SmtLiveDocuments.REPORT] == null || verdicts[SmtLiveDocuments.INTEGRITY] == null || verdicts[SmtLiveDocuments.RESULTS] == null)
        {
            m_output.WriteLine($"result   : FAILED: {string.Join(", ", missing)} did not come");
            return SmtLiveExitCodes.FAILED;
        }

        IReadOnlyList<string> alarms;
        try
        {
            alarms = new SmtLiveRunSummary(m_output).Print(Path.Combine(folder, SmtLiveDocuments.REPORT.File),
                Path.Combine(folder, SmtLiveDocuments.INTEGRITY.File), Path.Combine(folder, SmtLiveDocuments.RESULTS.File));
        }
        catch (JsonException exception)
        {
            m_output.WriteLine($"result   : FAILED: a document cannot be read: {exception.Message}");
            return SmtLiveExitCodes.FAILED;
        }

        if (missing.Count > 0)
        {
            m_output.WriteLine($"result   : FAILED: {string.Join(", ", missing)} did not come");
            return SmtLiveExitCodes.FAILED;
        }

        var invalid = SmtLiveDocuments.RUN.Where(document => verdicts[document]?.IsValid != true).Select(document => document.File).ToList();
        if (invalid.Count > 0)
        {
            m_output.WriteLine($"result   : FAILED: {string.Join(", ", invalid)} {(invalid.Count == 1 ? "does" : "do")} not hold to {(invalid.Count == 1 ? "its schema" : "their schemas")}");
            return SmtLiveExitCodes.FAILED;
        }

        if (alarms.Count > 0)
        {
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}result   : ALARM: {string.Join("; ", alarms)}");
            return SmtLiveExitCodes.ALARM;
        }

        m_output.WriteLine("result   : OK: the four documents hold to their schemas, nothing is alarming");
        return SmtLiveExitCodes.OK;
    }

    /// <summary>Says what stopped the tool's part, that the job goes on on the server, and how to fetch its documents.</summary>
    private void Left(ISmtLiveJob job, string folder, string what)
    {
        var target = SmtLiveFolder.HoldsDocuments(folder) ? FetchFolder(job.Id) : folder;
        m_output.WriteLine($"{SmtLiveRunSummary.LOUD}{what}");
        m_output.WriteLine($"{SmtLiveRunSummary.LOUD}the job {job.Id} goes on on the server; its documents, once it has ended: smt-live fetch {job.Id} --out {target}");
    }

    /// <summary>Writes <c>fleet.json</c>: what the run took from the tool's start to the documents on disk.</summary>
    private void WriteFacts(ISmtLiveJob job, SmtLiveJobEnd end, string folder, (string File, int Rows, long Bytes, string Sha256, TimeSpan Connected, TimeSpan Uploaded) set,
        TimeSpan ended, TimeSpan wall)
    {
        var facts = new SmtLiveFleetFacts
        {
            StartedUtc = m_clock.StartedUtc,
            Job = job.Id,
            Status = end.Status,
            SetFile = set.File,
            SetSha256 = set.Sha256,
            SetBytes = set.Bytes,
            Rows = set.Rows,
            Taken = Taken(Path.Combine(folder, SmtLiveDocuments.REPORT.File)),
            Connect = set.Connected,
            Upload = set.Uploaded - set.Connected,
            JobTime = ended - set.Uploaded,
            Fetch = wall - ended,
            Wall = wall,
            Poll = SmtLiveServerCloud.POLL_INTERVAL,
            SubmittedUtc = end.SubmittedUtc,
            ServerStartedUtc = end.StartedUtc,
            CompletedUtc = end.CompletedUtc
        };
        facts.Write(Path.Combine(folder, SmtLiveFleetFacts.FILE));
        m_output.WriteLine($"wall     : {Clock(wall)} from the tool's start to the documents on disk: connect {Clock(set.Connected)}, upload {Clock(set.Uploaded - set.Connected)}, " +
                           $"job {Clock(ended - set.Uploaded)}{ServerTimes(end)}, fetch {Clock(wall - ended)}; {SmtLiveFleetFacts.FILE} written");
    }

    /// <summary>The problems a run took, as its report says; 0 when the report does not say.</summary>
    private static long Taken(string reportPath)
    {
        try
        {
            using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
            return report.RootElement.TryGetProperty("problem_set", out var set) && set.TryGetProperty("accepted", out var accepted) && accepted.TryGetInt64(out var taken)
                ? taken
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>The job's queue and run by the server's clock, when it gave them.</summary>
    private static string ServerTimes(SmtLiveJobEnd end)
    {
        if (end.StartedUtc is not { } started || end.CompletedUtc is not { } completed)
            return "";

        var queue = end.SubmittedUtc is { } submitted ? $"queue {Clock(started - submitted)}, " : "";
        return $" (by the server's clock: {queue}run {Clock(completed - started)})";
    }

    private static string Clock(TimeSpan time)
    {
        return time.ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture);
    }

    /// <summary>Downloads one document and holds it to its schema; null when the job names no blob for it.</summary>
    private async Task<SmtLiveDocumentVerdict?> FetchAsync(ISmtLiveJob job, SmtLiveDocument document, SmtLiveSchema schema, string folder, CancellationToken ct)
    {
        var blob = await job.ResultAsync<Guid?>(document.Variable, ct);
        if (blob == null)
        {
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}{document.File,-16} DID NOT COME (the job's variable {document.Variable} names no blob)");
            return null;
        }

        var path = Path.Combine(folder, document.File);
        await m_server.DownloadAsync(blob.Value, path, new SmtLiveProgress(m_output, "download"), ct);

        var verdict = SmtLiveDocumentCheck.CheckFile(path, schema, document.Lines);
        var size = document.Lines ? $"{verdict.Items} row(s), {verdict.Bytes} bytes" : $"{verdict.Bytes} bytes";
        m_output.WriteLine(verdict.IsValid
            ? $"  {document.File,-16} {size}: valid ({schema.Name})"
            : $"{SmtLiveRunSummary.LOUD}{document.File,-16} {size}: DOES NOT HOLD TO {schema.Name} ({verdict.Faulty} faulty)");
        foreach (var error in verdict.Errors)
            m_output.WriteLine($"{SmtLiveRunSummary.LOUD}  {error}");

        return verdict;
    }

    #endregion
}
