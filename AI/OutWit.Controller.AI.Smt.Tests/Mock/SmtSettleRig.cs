using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Settlement;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The host's side of a run without nodes and without the solver: a problem set is read as a run
/// reads it, its problems are stored as packages, rows are made up for them as a node would write
/// them (each with its own digest, unless a test breaks it), and the host's rounds and settlement
/// run on them as the activities run them. Every result the rig makes is told to the host as
/// returned for the package it answers, as Grid 1.4.0 tells the return collection of a fan-out,
/// unless a test says it was returned for another package or turns the telling off
/// (<see cref="Binds"/>: a round filled as an older Grid fills it). What the tests see are the
/// model package's types and the documents' texts; the controller's types are met inside method
/// bodies only, and the rig's methods wait for the controller's tasks themselves: an awaited task
/// of a controller type would put that type into the shape of a test's state machine.
/// </summary>
internal sealed class SmtSettleRig : IDisposable
{
    #region Fields

    private readonly string m_folder;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>A rig with a blob store and a working folder of its own.</summary>
    public SmtSettleRig()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_settle_{Guid.NewGuid():N}");
        Blobs = new SmtTestBlobService(Path.Combine(m_folder, "blobs"));
        Directory.CreateDirectory(Work);
    }

    #endregion

    #region Problems

    /// <summary>The taken problems of a set, as a run reads them.</summary>
    /// <param name="set">The set's text.</param>
    /// <param name="options">The run's options.</param>
    /// <returns>The problems, with their indices.</returns>
    public static List<SmtProblemData> Read(string set, SmtOptionsData options)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(set));
        var reader = new SmtProblemSetReader(stream, options);
        var problems = new List<SmtProblemData>();
        while (reader.TryRead(out var row))
        {
            if (row.IsAccepted)
                problems.Add(row.Data);
        }

        return problems;
    }

    /// <summary>Stores problems as one package, as Smt.Split stores a package.</summary>
    /// <param name="problems">The problems.</param>
    /// <param name="options">The run's options.</param>
    /// <returns>The package's reference.</returns>
    public SmtTaskPackageData Publish(IReadOnlyList<SmtProblemData> problems, SmtOptionsData options)
    {
        return SmtTaskPackages.PublishAsync(problems, SmtOptionsRules.Resolve(options), Blobs, Path.Combine(Work, "packages")).GetAwaiter().GetResult();
    }

    /// <summary>Stores a set as a blob.</summary>
    /// <param name="set">The set's text.</param>
    /// <returns>The blob's local file.</returns>
    public string StoreSet(string set)
    {
        var blob = Blobs.UploadBytesAsync(Encoding.UTF8.GetBytes(set), "problems.jsonl").GetAwaiter().GetResult();
        return Blobs.GetLocalPathAsync(blob).GetAwaiter().GetResult();
    }

    #endregion

    #region Rows

    /// <summary>
    /// A node's answer to a package: the package's problems read from its blob, a row made of each
    /// by a function (the ticket, the problem, the version, the platform and the digest are put in
    /// by the rig unless the function's row breaks them on purpose), written and stored. The host
    /// is told that the result was returned for a task of the package, or of the one given, as
    /// Grid tells it (while <see cref="Binds"/> holds).
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="answer">The row of a problem: its status, verdict, models and the rest.</param>
    /// <param name="seal">Whether the rig puts the row's own digest in; false leaves the function's.</param>
    /// <param name="returnedFor">The package whose task the result came back for; null for the package it answers.</param>
    /// <returns>The result package's reference.</returns>
    public SmtResultPackageData Answer(SmtTaskPackageData package, Func<SmtProblemData, SmtResultData> answer, bool seal = true, SmtTaskPackageData? returnedFor = null)
    {
        var rows = new List<SmtResultData>();
        var problems = Blobs.GetLocalPathAsync(package.ProblemsBlobId).GetAwaiter().GetResult();
        using (var reader = new SmtTaskPackageReader(problems, package.ProblemCount))
        {
            while (reader.TryRead(out var problem))
            {
                var row = answer(problem!);
                row.Index = problem!.Index;
                row.Id = problem.Id;
                row.Hash = problem.Hash;
                row.Query = problem.Query;
                row.Ticket = row.Ticket.Length == 0 ? package.Ticket : row.Ticket;
                row.ControllerVersion = ControllerBuildInfo.VERSION;
                row.Platform = SmtResultRows.Platform;
                if (seal)
                    row.Digest = SmtResultDigest.Of(row);

                rows.Add(row);
            }
        }

        var path = Path.Combine(Work, $"{Guid.NewGuid():N}.wsp");
        using (var writer = new SmtResultPackageWriter(path))
        {
            foreach (var row in rows)
                writer.Write(row);

            writer.Complete();
        }

        var result = SmtPackageTransfer.UploadResultsAsync(Blobs, path, package.Ticket, rows.Count, NullLogger.Instance, CancellationToken.None).GetAwaiter().GetResult();
        if (Binds)
            SmtRunRig.Bind(returnedFor ?? package, result);

        return result;
    }

    /// <summary>A row that answers sat with a model (and the values asked, when given).</summary>
    /// <param name="model">The answer of (get-model).</param>
    /// <param name="values">The answer of (get-value ...), or null.</param>
    /// <returns>The row, without its problem and digest.</returns>
    public static SmtResultData Sat(string model, string? values = null)
    {
        return Row(SmtResultWords.OK, SmtResultWords.SAT, SmtResultWords.NONE, [model], values == null ? [] : [values]);
    }

    /// <summary>A row that answers unsat.</summary>
    /// <returns>The row, without its problem and digest.</returns>
    public static SmtResultData Unsat()
    {
        return Row(SmtResultWords.OK, SmtResultWords.UNSAT, SmtResultWords.NONE, [], []);
    }

    /// <summary>A row of a fault of the node: the wall-clock backstop ended its session.</summary>
    /// <returns>The row, without its problem and digest.</returns>
    public static SmtResultData Backstop()
    {
        var row = Row(SmtResultWords.NODE_FAULT, SmtResultWords.NONE, SmtResultWords.NONE, [], []);
        row.Code = "backstop";
        row.Message = "the session ran past the wall-clock backstop";
        return row;
    }

    /// <summary>A row of a node that refused the problem before a session started: a code of the refusals, and nothing run.</summary>
    /// <param name="code">The refusal's code.</param>
    /// <returns>The row, without its problem and digest.</returns>
    public static SmtResultData Refusal(string code)
    {
        var row = Row(SmtResultWords.REFUSED, SmtResultWords.NONE, SmtResultWords.NONE, [], []);
        row.Code = code;
        row.Message = "the node refused the problem";
        row.Checks = 0;
        row.ResourceCount = 0;
        row.Fuel = 0;
        row.PeakMemoryBytes = 0;
        return row;
    }

    /// <summary>A row of a status and a verdict with what a session gives with them.</summary>
    /// <param name="status">The status.</param>
    /// <param name="verdict">The verdict.</param>
    /// <param name="reason">The reason of an unknown.</param>
    /// <param name="models">The models.</param>
    /// <param name="values">The values.</param>
    /// <returns>The row, without its problem and digest.</returns>
    public static SmtResultData Row(string status, string verdict, string reason, List<string> models, List<string> values)
    {
        return new SmtResultData
        {
            Status = status,
            Code = "",
            Message = "",
            Verdict = verdict,
            Reason = reason,
            ReasonText = "",
            Proof = SmtResultWords.NONE,
            Models = models,
            Values = values,
            Checks = 1,
            ResourceCount = 1_000,
            Fuel = 1_000_000,
            PeakMemoryBytes = 1 << 20,
            Transcript = "",
            WallMs = 5
        };
    }

    #endregion

    #region Rounds

    /// <summary>What Smt.Repairs stores of a first fan-out.</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What came back.</param>
    /// <param name="rate">The sampling rate the script hands Smt.Repairs, or null for a call without it.</param>
    /// <returns>The packages of the repair round.</returns>
    public List<SmtTaskPackageData> Repairs(IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned, double? rate = null)
    {
        var main = SmtSettlement.ReadMainAsync(issued, returned, Blobs).GetAwaiter().GetResult();
        return SmtSettlement.PublishRepairsAsync(main, rate, Blobs, Path.Combine(Work, "repairs")).GetAwaiter().GetResult();
    }

    /// <summary>What Smt.Disputes stores of a first fan-out and a repair round.</summary>
    /// <param name="issued">The packages of the first fan-out.</param>
    /// <param name="returned">What came back.</param>
    /// <param name="repairs">The packages of the repair round.</param>
    /// <param name="repaired">What came back of them.</param>
    /// <param name="packageSteps">What the host's evaluator spends on one result package; null for the host's own number.</param>
    /// <returns>The packages of the dispute round.</returns>
    public List<SmtTaskPackageData> Disputes(IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> repairs, IReadOnlyList<SmtResultPackageData> repaired, long? packageSteps = null)
    {
        using var spool = new SmtSpool(Path.Combine(Work, "spool"));
        var main = SmtSettlement.ReadMainAsync(issued, returned, Blobs, spool).GetAwaiter().GetResult();
        var repair = SmtSettlement.ReadRepairAsync(repairs, repaired, Blobs, spool).GetAwaiter().GetResult();
        return SmtSettlement.PublishDisputesAsync(main, repair, spool, Blobs, Path.Combine(Work, "disputes"), packageSteps ?? SmtPackageBudget.DEFAULT_STEPS).GetAwaiter().GetResult().Packages;
    }

    /// <summary>
    /// What the host's evaluator spends on each result package of a first fan-out when it checks
    /// every row that says sat, as Smt.Disputes checks them.
    /// </summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What came back.</param>
    /// <param name="packageSteps">What the host's evaluator spends on one result package at the most.</param>
    /// <returns>The steps each result package spent, in the order the host took them in.</returns>
    public List<long> Spent(IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned, long packageSteps)
    {
        using var spool = new SmtSpool(Path.Combine(Work, "spool"));
        var main = SmtSettlement.ReadMainAsync(issued, returned, Blobs, spool).GetAwaiter().GetResult();
        SmtSettlement.CheckAsync([main], spool, Blobs, packageSteps, CancellationToken.None).GetAwaiter().GetResult();
        return main.Accepted.Select(package => package.Rows[0].Budget.Spent).ToList();
    }

    /// <summary>What Smt.Settle writes of a run: its four documents and the ledger's section of full models.</summary>
    /// <param name="set">The set's text.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="issued">The packages of the first fan-out.</param>
    /// <param name="returned">What came back.</param>
    /// <param name="repairs">The packages of the repair round.</param>
    /// <param name="repaired">What came back of them.</param>
    /// <param name="disputes">The packages of the dispute round, or null for a script that ran none.</param>
    /// <param name="settled">What came back of them.</param>
    /// <param name="rate">The sampling rate the script stated, or null.</param>
    /// <param name="packageSteps">What the host's evaluator spends on one result package; null for the host's own number.</param>
    /// <returns>The documents' texts.</returns>
    public SmtSettledDocuments Settle(string set, SmtOptionsData options,
        IReadOnlyList<SmtTaskPackageData> issued, IReadOnlyList<SmtResultPackageData> returned,
        IReadOnlyList<SmtTaskPackageData> repairs, IReadOnlyList<SmtResultPackageData> repaired,
        IReadOnlyList<SmtTaskPackageData>? disputes, IReadOnlyList<SmtResultPackageData>? settled, double? rate, long? packageSteps = null)
    {
        var path = StoreSet(set);
        var folder = Path.Combine(Work, $"settle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        (IReadOnlyList<SmtTaskPackageData> Issued, IEnumerable<SmtResultPackageData?> Returned)? dispute = disputes == null ? null : (disputes, settled ?? []);
        var bounds = SmtSettlementBounds.DEFAULT with { PackageSteps = packageSteps ?? SmtPackageBudget.DEFAULT_STEPS };
        var ledger = SmtRunSettlement.SettleAsync(path, options, (issued, returned), (repairs, repaired), dispute, Blobs, folder, rate, bounds).GetAwaiter().GetResult().LedgerPath;

        return new SmtSettledDocuments(Section(ledger, SmtLedger.RESULTS), Section(ledger, SmtLedger.REPORT), Section(ledger, SmtLedger.INTEGRITY),
            Section(ledger, SmtLedger.PROVENANCE), Section(ledger, SmtLedger.MODELS));
    }

    private static string Section(string ledger, int section)
    {
        return Encoding.UTF8.GetString(SmtLedger.Read(ledger, section));
    }

    #endregion

    #region IDisposable

    /// <summary>Removes the rig's folder.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Properties

    /// <summary>The rig's blob store.</summary>
    public SmtTestBlobService Blobs { get; }

    /// <summary>
    /// Whether the host is told which package each result the rig makes was returned for, as Grid
    /// 1.4.0 tells it; false fills the rounds as an older Grid does, or a script that fills the
    /// collections another way. True unless a test turns it off.
    /// </summary>
    public bool Binds { get; set; } = true;

    private string Work => Path.Combine(m_folder, "work");

    #endregion
}
