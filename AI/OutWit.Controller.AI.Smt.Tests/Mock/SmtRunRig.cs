using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Documents;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests of a run meet the host side of the controller: a result package forged from an
/// honest one, a package a node could not work on, which package a result was returned for (as
/// Grid tells it), the planner's numbers, the sampling rule, a preflight. As in
/// <see cref="SmtNodeRig"/>, the controller's types are met inside method bodies only: what a test
/// sees are the model package's types, words, numbers and texts.
/// </summary>
internal static class SmtRunRig
{
    #region Constants

    // The name of the return collection the rig tells, as a script would name it.
    private const string RETURNED = "returned";

    #endregion

    #region Binding

    /// <summary>
    /// Tells the host that a result was returned for a task of a package, the way Grid 1.4.0 tells
    /// the return collection of a fan-out: through the collection's own method, with the package
    /// and the result themselves.
    /// </summary>
    /// <param name="package">The package the task was built from.</param>
    /// <param name="result">The result the node returned for it.</param>
    public static void Bind(SmtTaskPackageData package, SmtResultPackageData result)
    {
        new WitVariableSmtResultPackageCollection(RETURNED).OnGridAnswer(package, result);
    }

    /// <summary>Tells the collection's method what Grid never gives it (no item, no value, other types), as a test of what it does with that.</summary>
    /// <param name="item">The item.</param>
    /// <param name="value">The value.</param>
    public static void Tell(object? item, object? value)
    {
        new WitVariableSmtResultPackageCollection(RETURNED).OnGridAnswer(item, value);
    }

    /// <summary>The ticket of the package the host was told a result was returned for.</summary>
    /// <param name="result">The result, the very object.</param>
    /// <returns>The ticket; null when the host was told nothing of this result.</returns>
    public static string? BoundTicket(SmtResultPackageData result)
    {
        return SmtResultBindings.TicketOf(result);
    }

    /// <summary>The indices of the problems a package holds, read from its blob as the host reads them.</summary>
    /// <param name="blobs">The blob service.</param>
    /// <param name="package">The package.</param>
    /// <returns>The indices, in ascending order.</returns>
    public static List<int> Indices(IWitBlobService blobs, SmtTaskPackageData package)
    {
        return SmtTaskBounds.LoadAsync(package, blobs).GetAwaiter().GetResult().Indices.Order().ToList();
    }

    /// <summary>
    /// A result made in place of another one (a forgery, a node's word that it could not work on
    /// the package) comes back for the same task: the host is told so, when it was told it of the
    /// other one.
    /// </summary>
    private static SmtResultPackageData BoundAs(SmtResultPackageData made, SmtResultPackageData original)
    {
        if (BoundTicket(original) is { } ticket)
            Bind(new SmtTaskPackageData { Ticket = ticket }, made);

        return made;
    }

    #endregion

    #region Forging

    /// <summary>
    /// What a node that lies makes of an honest result package: its rows read, changed as told,
    /// written and stored as a node stores them, under the honest package's ticket. The rig waits
    /// for the blob store itself (see <see cref="SmtSettleRig"/>).
    /// </summary>
    /// <param name="blobs">The blob service.</param>
    /// <param name="honest">The honest reference.</param>
    /// <param name="lie">What is done to the rows: a lie that wants its rows to hold recomputes their digests (<see cref="SmtResultDigest.Of"/>).</param>
    /// <returns>The forged reference.</returns>
    public static SmtResultPackageData Forge(IWitBlobService blobs, SmtResultPackageData honest, Action<List<SmtResultData>> lie)
    {
        var source = blobs.GetLocalPathAsync(honest.ResultsBlobId).GetAwaiter().GetResult();
        var rows = SmtResultPackageReader.Read(source, SmtPackageCodec.MAX_PACKAGE_PROBLEMS).ToList();
        lie(rows);

        var path = Path.Combine(Path.GetTempPath(), $"witai_smt_lie_{Guid.NewGuid():N}.wsp");
        try
        {
            using (var writer = new SmtResultPackageWriter(path))
            {
                foreach (var row in rows)
                    writer.Write(row);

                writer.Complete();
            }

            return BoundAs(SmtPackageTransfer.UploadResultsAsync(blobs, path, honest.Ticket, rows.Count, NullLogger.Instance, CancellationToken.None).GetAwaiter().GetResult(), honest);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A package a node says it could not work on: the ticket, a code, words, and no blob.</summary>
    /// <param name="honest">The honest reference (its ticket is kept).</param>
    /// <param name="code">The fault's code.</param>
    /// <returns>The reference.</returns>
    public static SmtResultPackageData Fault(SmtResultPackageData honest, string code)
    {
        return BoundAs(new SmtResultPackageData { Ticket = honest.Ticket, FaultCode = code, FaultMessage = "this node could not work on the package" }, honest);
    }

    /// <summary>A row of a fault of the node in place of a row: what the wall-clock backstop leaves, its digest its own.</summary>
    /// <param name="row">The row; changed.</param>
    public static void Backstop(SmtResultData row)
    {
        row.Status = SmtResultWords.NODE_FAULT;
        row.Code = "backstop";
        row.Message = "the session ran past the wall-clock backstop";
        row.Verdict = SmtResultWords.NONE;
        row.Reason = SmtResultWords.NONE;
        row.ReasonText = "";
        row.Proof = SmtResultWords.NONE;
        row.Count = 0;
        row.Complete = false;
        row.LimitReached = false;
        row.Models = [];
        row.Values = [];
        row.Digest = SmtResultDigest.Of(row);
    }

    #endregion

    #region Numbers

    /// <summary>How many packages of a fan-out a rate asks to be solved twice, by the rule Grid.Replicas selects by.</summary>
    /// <param name="packages">The packages.</param>
    /// <param name="rate">The rate.</param>
    /// <returns>The packages.</returns>
    public static int Selected(int packages, double rate)
    {
        return SmtSampling.Selected(packages, rate);
    }

    /// <summary>The most problems a package of a run of a number of problems holds.</summary>
    /// <param name="problems">The problems.</param>
    /// <returns>The number.</returns>
    public static int ProblemsPerPackage(long problems)
    {
        return SmtPackagePlanner.ProblemsPerPackage(problems);
    }

    /// <summary>The most bytes of models a row of the results shows.</summary>
    public static int MaxModelBytes => SmtResultsWriter.MAX_MODEL_BYTES;

    /// <summary>The reasons a result package is refused for, in the order the integrity document lists them.</summary>
    public static IReadOnlyList<string> RejectReasons => SmtRoundIntake.REASONS;

    /// <summary>The reason of a repair of a problem whose package was asked for twice and came back with fewer than two answers.</summary>
    public static string CopiesShort => SmtRulings.COPIES_SHORT;

    /// <summary>The schema names of the four documents: results rows have none; the report, the integrity and the provenance.</summary>
    public static (string Report, string Integrity, string Provenance) Schemas => (SmtReportWriter.SCHEMA, SmtIntegrityWriter.SCHEMA, SmtProvenanceWriter.SCHEMA);

    #endregion

    #region Preflight

    /// <summary>The preflight of a set's file and options, made as the activity makes it.</summary>
    /// <param name="path">The set's file.</param>
    /// <param name="options">The options, or null.</param>
    /// <returns>The preflight.</returns>
    public static SmtPreflightData Preflight(string path, SmtOptionsData? options)
    {
        return SmtPreflightReport.Of(path, options);
    }

    #endregion
}
