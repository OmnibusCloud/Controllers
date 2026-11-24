using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Smt.Activities;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// How the tests meet the node side of the controller: a problem's row, a package of problems
/// written and read, a package solved by the pool, the refusals of a package, the degree rule, the
/// diagnostics, the capabilities, the benchmark. As in <see cref="SmtSolverRig"/>, the controller's
/// types are met inside method bodies only: what a test sees are the model package's types, words,
/// numbers and texts, so no test type brings the controller's assembly in when the tests are looked
/// for.
/// </summary>
internal static class SmtNodeRig
{
    #region Constants

    /// <summary>A ticket in the form of one.</summary>
    public const string TICKET = "0123456789abcdef0123456789abcdef";

    #endregion

    #region Rows

    /// <summary>
    /// The row a node makes of a problem, solved in the process's sandbox (or with something
    /// changed that the options cannot change).
    /// </summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="ticket">The package's ticket.</param>
    /// <param name="setup">What is changed, or null.</param>
    /// <returns>The row.</returns>
    public static SmtResultData Row(SmtProblemData problem, SmtOptionsData options, string ticket = TICKET, SmtSolverSetup? setup = null)
    {
        return SmtResultRows.Of(problem, (SmtSolveOutcome)SmtSolverRig.Raw(problem, options, setup), ticket);
    }

    /// <summary>
    /// A row in a few words, as <see cref="SmtSolved.Words"/> writes an outcome: the status and its
    /// code, or <c>ok</c>, the verdict, the reason, for an enumeration the count and its end, for a
    /// proof what it came to.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The words.</returns>
    public static string Words(SmtResultData row)
    {
        if (row.Status != SmtResultWords.OK)
            return row.Code.Length > 0 ? $"{row.Status} {row.Code}" : row.Status;

        var words = $"ok {row.Verdict}";
        if (row.Reason != SmtResultWords.NONE)
            words += $" {row.Reason}";

        if (row.Query == SmtQuery.Enumerate)
            words += $" {row.Count} {(row.Complete ? "complete" : row.LimitReached ? "limit" : "incomplete")}";

        return row.Proof == SmtResultWords.NONE ? words : $"{words} {row.Proof}";
    }

    /// <summary>The words the controller writes for its statuses, verdicts, reasons and proofs, every value of each.</summary>
    /// <returns>The four lists, in the order of the controller's values.</returns>
    public static (string[] Statuses, string[] Verdicts, string[] Reasons, string[] Proofs) ControllerWords()
    {
        var statuses = new List<string>();
        foreach (var status in Enum.GetValues<SmtSolveStatus>())
            statuses.Add(SmtSolveWords.Of(status));

        var verdicts = new List<string>();
        foreach (var verdict in Enum.GetValues<SmtVerdict>())
            verdicts.Add(SmtSolveWords.Of(verdict));

        var reasons = new List<string>();
        foreach (var reason in Enum.GetValues<SmtUnknownReason>())
            reasons.Add(SmtSolveWords.Of(reason));

        var proofs = new List<string>();
        foreach (var proof in Enum.GetValues<SmtProofResult>())
            proofs.Add(SmtSolveWords.Of(proof));

        return ([.. statuses], [.. verdicts], [.. reasons], [.. proofs]);
    }

    #endregion

    #region Packages

    /// <summary>Writes problems as a package file and makes its reference, without a blob.</summary>
    /// <param name="problems">The problems.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="path">The file.</param>
    /// <returns>The reference; its blob id is a new one that names nothing.</returns>
    public static SmtTaskPackageData Write(IReadOnlyList<SmtProblemData> problems, SmtOptionsData options, string path)
    {
        var package = SmtTaskPackages.Write(problems, options, path);
        package.ProblemsBlobId = Guid.NewGuid();
        return package;
    }

    /// <summary>Writes problems as a package and stores it in a blob service.</summary>
    /// <param name="problems">The problems.</param>
    /// <param name="options">The run's options.</param>
    /// <param name="blobs">The blob service.</param>
    /// <param name="folder">A folder of the test's own.</param>
    /// <returns>The reference.</returns>
    public static Task<SmtTaskPackageData> PublishAsync(IReadOnlyList<SmtProblemData> problems, SmtOptionsData options, IWitBlobService blobs, string folder)
    {
        return SmtTaskPackages.PublishAsync(problems, options, blobs, folder);
    }

    /// <summary>Writes a package file of raw frames, for packages a host of this controller would never write.</summary>
    /// <param name="path">The file.</param>
    /// <param name="kind">The kind byte of the header.</param>
    /// <param name="frames">The frames' images; a frame whose claimed length is given is written with that length in its head.</param>
    public static void WriteFrames(string path, byte kind, IEnumerable<(byte[] Image, int? ClaimedLength)> frames)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        SmtPackageCodec.WriteHeader(file, kind);
        using var brotli = new BrotliStream(file, new BrotliCompressionOptions { Quality = 1 }, leaveOpen: true);
        foreach (var (image, claimed) in frames)
        {
            if (claimed == null)
            {
                SmtPackageCodec.WriteFrame(brotli, image);
                continue;
            }

            brotli.Write(BitConverter.GetBytes(claimed.Value));
            brotli.Write(image);
        }
    }

    /// <summary>The kind byte of a package of problems.</summary>
    public static byte ProblemsKind => SmtPackageCodec.KIND_PROBLEMS;

    /// <summary>The kind byte of a package of results.</summary>
    public static byte ResultsKind => SmtPackageCodec.KIND_RESULTS;

    /// <summary>
    /// Reads a package of problems as a node reads it.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="maxProblems">The most problems it may hold.</param>
    /// <param name="maxImageBytes">The most bytes it may unpack to; 0 for the controller's bound.</param>
    /// <returns>The problems read, and the code of the refusal that ended the reading, or null.</returns>
    public static (List<SmtProblemData> Problems, string? Fault) ReadProblems(string path, int maxProblems, long maxImageBytes = 0)
    {
        var problems = new List<SmtProblemData>();
        try
        {
            using var reader = new SmtTaskPackageReader(path, maxProblems, maxImageBytes);
            while (reader.TryRead(out var problem))
                problems.Add(problem!);

            return (problems, null);
        }
        catch (SmtPackageException exception)
        {
            return (problems, exception.Code);
        }
    }

    /// <summary>Reads a package of results as a host reads it.</summary>
    /// <param name="path">The file.</param>
    /// <param name="maxRows">The most rows it may hold.</param>
    /// <param name="maxImageBytes">The most bytes it may unpack to; 0 for the controller's bound.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="InvalidDataException">The package is refused.</exception>
    public static List<SmtResultData> ReadRows(string path, int maxRows, long maxImageBytes = 0)
    {
        return SmtResultPackageReader.Read(path, maxRows, maxImageBytes).ToList();
    }

    /// <summary>Writes rows as a node writes them.</summary>
    /// <param name="path">The file.</param>
    /// <param name="rows">The rows.</param>
    public static void WriteRows(string path, IEnumerable<SmtResultData> rows)
    {
        using var writer = new SmtResultPackageWriter(path);
        foreach (var row in rows)
            writer.Write(row);

        writer.Complete();
    }

    /// <summary>What a node says of a package's reference before it downloads anything.</summary>
    /// <param name="package">The reference.</param>
    /// <returns>The fault's code and words, or null when the node takes it.</returns>
    public static (string Code, string Message)? Refusal(SmtTaskPackageData package)
    {
        try
        {
            SmtPackageRunner.EnsureAcceptable(package);
            return null;
        }
        catch (SmtPackageException exception)
        {
            return (exception.Code, exception.Message);
        }
    }

    /// <summary>What a node says of a downloaded blob against its reference.</summary>
    /// <param name="package">The reference.</param>
    /// <param name="path">The blob's file.</param>
    /// <returns>The fault's code and words, or null when the blob is the package.</returns>
    public static (string Code, string Message)? BlobRefusal(SmtTaskPackageData package, string path)
    {
        try
        {
            SmtPackageRunner.EnsureBlob(package, path);
            return null;
        }
        catch (SmtPackageException exception)
        {
            return (exception.Code, exception.Message);
        }
    }

    /// <summary>What a node says of a package when its machine has given numbers.</summary>
    /// <param name="package">The reference.</param>
    /// <param name="unavailable">Why the process has no sandbox, or null.</param>
    /// <param name="cores">The machine's cores.</param>
    /// <param name="availableBytes">Its available memory.</param>
    /// <returns>The degree, or the fault's code.</returns>
    public static (int Degree, string? Fault) Prepare(SmtTaskPackageData package, string? unavailable, int cores, long availableBytes)
    {
        try
        {
            return (SmtPackageRunner.Prepare(package, unavailable, session => SmtSandboxDegree.Compute(cores, availableBytes, session)).Degree, null);
        }
        catch (SmtPackageException exception)
        {
            return (0, exception.Code);
        }
    }

    /// <summary>
    /// Solves a checked package with the pool in the process's sandbox and reads its rows back.
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <param name="problemsPath">The package's file.</param>
    /// <param name="degree">The sessions at once.</param>
    /// <param name="resultsPath">The file of the rows.</param>
    /// <param name="problemDone">Called after each row.</param>
    /// <param name="cancellationToken">The job's token.</param>
    /// <returns>The rows, in the order written.</returns>
    public static List<SmtResultData> RunPackage(SmtTaskPackageData package, string problemsPath, int degree, string resultsPath, Action? problemDone = null, CancellationToken cancellationToken = default)
    {
        var sandbox = SmtSandboxHost.Get(out var unavailable) ?? throw new InvalidOperationException(unavailable);
        var written = SmtPackageRunner.Run(package, problemsPath, sandbox, degree, resultsPath, problemDone, NullLogger.Instance, cancellationToken);
        Assert.That(written.Rows, Is.EqualTo(package.ProblemCount), "a row for every problem");
        return ReadRows(written.Path, package.ProblemCount);
    }

    /// <summary>What a node says when a package's blob cannot be fetched: one attempt, no waiting.</summary>
    /// <param name="blobs">The node's blob service.</param>
    /// <param name="package">The reference.</param>
    /// <returns>The fault's code, or null when the blob was fetched.</returns>
    public static async Task<string?> FetchFaultAsync(IWitBlobService blobs, SmtTaskPackageData package)
    {
        try
        {
            await SmtPackageTransfer.FetchAsync(blobs, package, NullLogger.Instance, CancellationToken.None, [TimeSpan.Zero]);
            return null;
        }
        catch (SmtPackageException exception)
        {
            return exception.Code;
        }
    }

    /// <summary>The digest of indices a package's reference carries.</summary>
    /// <param name="indices">The indices.</param>
    /// <returns>Lower-case hex.</returns>
    public static string IndexDigest(IEnumerable<int> indices)
    {
        return SmtTickets.IndexDigest(indices);
    }

    /// <summary>A new ticket.</summary>
    /// <returns>32 lower-case hex characters.</returns>
    public static string NewTicket()
    {
        return SmtTickets.New();
    }

    /// <summary>Whether a text has the form of a ticket.</summary>
    /// <param name="ticket">The text.</param>
    /// <returns>True for one.</returns>
    public static bool IsTicket(string? ticket)
    {
        return SmtTickets.IsWellFormed(ticket);
    }

    /// <summary>The longest a running session goes on after its package was cancelled, under options.</summary>
    /// <param name="options">The options.</param>
    /// <returns>Milliseconds.</returns>
    public static int CancelWorstCaseMs(SmtOptionsData options)
    {
        return SmtPackageRunner.CancelWorstCaseMs(options);
    }

    #endregion

    #region Degree

    /// <summary>The degree rule for a machine's numbers.</summary>
    /// <param name="cores">Logical cores.</param>
    /// <param name="availableBytes">Available memory; 0 when unknown.</param>
    /// <param name="sessionBytes">One session's memory.</param>
    /// <returns>The degree and what decided it.</returns>
    public static (int Degree, string Reason) Degree(int cores, long availableBytes, long sessionBytes)
    {
        var degree = SmtSandboxDegree.Compute(cores, availableBytes, sessionBytes);
        return (degree.Degree, degree.Reason);
    }

    /// <summary>One session's memory under options.</summary>
    /// <param name="options">The options.</param>
    /// <returns>Bytes.</returns>
    public static long SessionBytes(SmtOptionsData options)
    {
        return SmtSandboxDegree.SessionBytes(options);
    }

    /// <summary>
    /// The least memory of a machine the activity that solves a package declares, read off its
    /// requirement attributes as the server's planner reads them.
    /// </summary>
    /// <returns>Its <c>RequiresResources.MinRamMb</c>, every such attribute of it; empty when it declares none.</returns>
    public static List<long> SolvePackageMinRamMb()
    {
        return typeof(WitActivitySmtSolvePackage).GetTypeInfo().GetCustomAttributes().OfType<RequiresResourcesAttribute>().Select(requirement => requirement.MinRamMb).ToList();
    }

    /// <summary>The host's cap of the solver's linear memory for a memory option.</summary>
    /// <param name="megabytes">The option.</param>
    /// <returns>Bytes.</returns>
    public static long HostMemoryBytes(int megabytes)
    {
        return SmtProfile.HostMemoryBytes(megabytes);
    }

    /// <summary>The margin of a session beside its two caps.</summary>
    public static long SessionMarginBytes => SmtSandboxDegree.SESSION_MARGIN_BYTES;

    /// <summary>The least memory of a machine for the planner to send it a package, MiB.</summary>
    public static long MinRamMb => SmtSandboxDegree.MIN_RAM_MB;

    /// <summary>The most sessions at once.</summary>
    public static int MaxDegree => SmtSandboxDegree.MAX_DEGREE;

    /// <summary>The share of the available memory the sessions may claim.</summary>
    public static double RamHeadroom => SmtSandboxDegree.RAM_HEADROOM;

    /// <summary>The platform a row names.</summary>
    public static string Platform => SmtResultRows.Platform;

    /// <summary>The controller's version.</summary>
    public static string ControllerVersion => ControllerBuildInfo.VERSION;

    /// <summary>The most problems a package holds.</summary>
    public static int MaxPackageProblems => SmtPackageCodec.MAX_PACKAGE_PROBLEMS;

    /// <summary>The most bytes a package of problems may be.</summary>
    public static long MaxPackageBytes => SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES;

    #endregion
}
