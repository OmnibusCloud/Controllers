using System.Text.Json;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Server;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>run</c>: uploads a problem set, runs <c>SmtRun</c> with the options, waits with the job's
/// progress, downloads the four documents (results, report, integrity, provenance) into a
/// folder, holds each to its schema, and prints the report's summary. Options out of their
/// ranges are refused here, before anything is sent: the controller would refuse the run.
/// </summary>
internal sealed class SmtLiveRun
{
    #region Constants

    private const string SCRIPT = "SmtRun";
    private const string FOLDER_PREFIX = "smt-run-";
    private const int FOLDER_ID_CHARACTERS = 12;

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
    public SmtLiveRun(ISmtLiveServer server, TextWriter output)
    {
        m_server = server;
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs a problem set.
    /// </summary>
    /// <param name="path">The problem set.</param>
    /// <param name="options">The run's options as given.</param>
    /// <param name="folder">Where the documents go; null for <c>smt-run-</c> and the head of the job's id, in the current folder.</param>
    /// <param name="ct">Stops the local waiting; the job goes on on the server.</param>
    /// <returns>
    /// The exit code: 0 when the run completed, every document came and holds to its schema, and
    /// nothing is alarming; 3 when something is (<see cref="SmtLiveRunSummary"/>); 2 when the
    /// options are out of range; 1 otherwise.
    /// </returns>
    public async Task<int> RunAsync(string path, SmtOptionsData options, string? folder, CancellationToken ct)
    {
        m_output.WriteLine($"options  : {SmtLiveInput.Describe(options)}");
        var refused = SmtOptionsRules.Check(options);
        if (refused.Count > 0)
        {
            m_output.WriteLine("the options are out of range; nothing was sent:");
            foreach (var problem in refused)
                m_output.WriteLine($"  {problem}");

            return SmtLiveExitCodes.USAGE;
        }

        var schemas = SmtLiveDocuments.RUN.ToDictionary(document => document, SmtLiveDocuments.SchemaOf);
        var blob = await SmtLiveInput.UploadAsync(m_server, path, m_output, ct);

        var job = await m_server.SubmitAsync(SCRIPT, [blob, options], ct);
        m_output.WriteLine($"{SCRIPT} submitted: job {job.Id}");

        var end = await job.WaitAsync(new SmtLiveProgress(m_output, "progress"), ct);
        if (!end.Completed)
        {
            m_output.WriteLine($"  {end.Status} after {end.Duration:hh\\:mm\\:ss}: {end.Error}");
            return SmtLiveExitCodes.FAILED;
        }

        m_output.WriteLine($"  completed in {end.Duration:hh\\:mm\\:ss}");

        var target = folder ?? FOLDER_PREFIX + job.Id.ToString("N")[..FOLDER_ID_CHARACTERS];
        Directory.CreateDirectory(target);
        m_output.WriteLine($"documents: {Path.GetFullPath(target)}");

        var held = true;
        var came = new HashSet<SmtLiveDocument>();
        foreach (var document in SmtLiveDocuments.RUN)
        {
            var verdict = await FetchAsync(job, document, schemas[document], target, ct);
            if (verdict != null)
                came.Add(document);

            held &= verdict?.IsValid == true;
        }

        if (!came.Contains(SmtLiveDocuments.REPORT) || !came.Contains(SmtLiveDocuments.INTEGRITY) || !came.Contains(SmtLiveDocuments.RESULTS))
        {
            m_output.WriteLine("result   : FAILED: a document did not come");
            return SmtLiveExitCodes.FAILED;
        }

        IReadOnlyList<string> alarms;
        try
        {
            alarms = new SmtLiveRunSummary(m_output).Print(Path.Combine(target, SmtLiveDocuments.REPORT.File),
                Path.Combine(target, SmtLiveDocuments.INTEGRITY.File), Path.Combine(target, SmtLiveDocuments.RESULTS.File));
        }
        catch (JsonException exception)
        {
            m_output.WriteLine($"result   : FAILED: a document cannot be read: {exception.Message}");
            return SmtLiveExitCodes.FAILED;
        }

        if (!held)
        {
            m_output.WriteLine("result   : FAILED: a document does not hold to its schema");
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
