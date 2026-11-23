using System.Globalization;
using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Server;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Commands;

/// <summary>
/// <c>preflight</c>: uploads a problem set, runs <c>SmtPreflight</c> with the options, and prints
/// what it says: the rows taken and refused (by code, the first refusals in full), the taken
/// problems by logic, query and expected verdict, the options as the controller checks them, the
/// budget of a problem, the memory a node needs for one session, the upper bound of the work and
/// the packages a run would make. Nothing is sent to a node.
/// </summary>
internal sealed class SmtLivePreflight
{
    #region Constants

    private const string SCRIPT = "SmtPreflight";
    private const string RESULT = "result";
    private const int SHOWN_REFUSALS = 20;
    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

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
    public SmtLivePreflight(ISmtLiveServer server, TextWriter output)
    {
        m_server = server;
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Runs the preflight of a problem set.
    /// </summary>
    /// <param name="path">The problem set.</param>
    /// <param name="options">The run's options as given.</param>
    /// <param name="ct">Stops the local waiting.</param>
    /// <returns>The exit code: 0 when a run would start and the options are taken, 1 otherwise.</returns>
    public async Task<int> RunAsync(string path, SmtOptionsData options, CancellationToken ct)
    {
        m_output.WriteLine($"options  : {SmtLiveInput.Describe(options)}");
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
        var preflight = await job.ResultAsync<SmtPreflightData>(RESULT, ct);
        if (preflight == null)
        {
            m_output.WriteLine("  no preflight came back");
            return SmtLiveExitCodes.FAILED;
        }

        Print(preflight);
        return preflight.Runnable && preflight.OptionProblems.Count == 0 ? SmtLiveExitCodes.OK : SmtLiveExitCodes.FAILED;
    }

    private void Print(SmtPreflightData p)
    {
        Line("runnable", p.Runnable ? "yes" : "NO: a run would not start", !p.Runnable);
        Line("rows", $"{p.Rows}: {p.Accepted} taken, {p.Refused} refused; {p.BytesRead} bytes read, sha256 {p.Sha256}");
        if (p.Refused > 0)
        {
            Line("refused", Counts(p.RefusalCodes));
            foreach (var refusal in p.Refusals.Take(SHOWN_REFUSALS))
            {
                var command = refusal.Command.Length > 0 ? $" ({refusal.Command})" : "";
                var line = refusal.Line > 0 ? $", line {refusal.Line}" : "";
                Line("", $"row {refusal.Row} ({refusal.File}){line}{command}: {refusal.Code}: {refusal.Reason}");
            }

            if (p.Refused > Math.Min(p.Refusals.Count, SHOWN_REFUSALS))
                Line("", $"and {p.Refused - Math.Min(p.Refusals.Count, SHOWN_REFUSALS)} more");
        }

        Line("logics", Counts(p.Logics) + (p.OtherLogics > 0 ? $", {p.OtherLogics} of other logics" : ""));
        Line("queries", Counts(p.Queries));
        Line("expected", Counts(p.Expected));
        Line("stripped", Counts(p.Stripped));
        Line("licences", Counts(p.Licenses) + (p.OtherLicenses > 0 ? $", {p.OtherLicenses} of other licences" : ""));
        Line("options", $"profile {p.Profile}, rlimit {p.ResourceLimit}, hard cap {p.HardCapSeconds} s (fuel {p.Fuel}), memory {p.MemoryMegabytes} MB, " +
                        $"max models {p.MaxModels}, seed {p.Seed}, sampling rate {p.SamplingRate.ToString("0.###", CultureInfo.InvariantCulture)}");

        if (p.OptionProblems.Count == 0)
        {
            Line("checked", "the options are taken");
        }
        else
        {
            Line("checked", "THE OPTIONS ARE REFUSED (the set was read with each such option at its default)", true);
            foreach (var problem in p.OptionProblems)
                Line("", problem, true);
        }

        Line("session", $"a node needs {p.SessionMemoryBytes / BYTES_PER_MIB:F0} MiB available for one session");
        Line("bound", $"{p.UpperBoundSeconds} s of the reference machine: the taken problems times the hard cap, a bound and not an estimate");
        Line("packages", $"{p.Packages}, at most {p.ProblemsPerPackage} problem(s) each (before any copy)");
        foreach (var message in p.Messages)
            Line("", message);
    }

    private static string Counts(IReadOnlyList<SmtCountData> counts)
    {
        return counts.Count == 0 ? "-" : string.Join(", ", counts.Select(count => $"{(count.Name.Length == 0 ? "(none)" : count.Name)} {count.Count}"));
    }

    private void Line(string label, string text, bool loud = false)
    {
        var prefix = loud ? SmtLiveRunSummary.LOUD : "  ";
        m_output.WriteLine(label.Length == 0 ? $"{prefix}{"",-10}  {text}" : $"{prefix}{label,-10}: {text}");
    }

    #endregion
}
