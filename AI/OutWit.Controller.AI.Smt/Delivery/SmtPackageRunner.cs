using Microsoft.Extensions.Logging;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Sandbox;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// What a node does with one package of problems, between the download and the upload. First it
/// checks everything it can know before it solves anything: the package was made for this
/// controller version, has a ticket and is within what a node takes (before the download); the
/// blob is the one its reference names and holds exactly the problems the reference names (after
/// it); this process has a sandbox, and its memory holds at least one session under the package's
/// options. If any of that fails the package is refused whole, with its code
/// (<see cref="SmtPackageException"/>): nothing was solved and the work goes elsewhere. Then the
/// problems are solved by a pool of sessions, and each row is written to the package of results as
/// its session ends: a node holds the rows of the sessions that are running, never of the package.
/// A problem that fails on its own (a refusal, an error of the solver's, a crash) is a row with its
/// status, never a failed package.
/// <para>
/// <b>Cancel.</b> Once the job's token is cancelled no problem is handed out any more, and every
/// running session ends at the solver's next read of its input (<see cref="SmtSession"/>); the
/// package returns nothing and is sent again elsewhere. A session that is inside a check when the
/// cancel comes ends when the check does: by its answer, by its resource limit, by its fuel, or by
/// the wall-clock backstop. Fuel ends it within the hard cap on a machine at the reference rate
/// (60 s by default, 600 s at most); on any machine the backstop ends it within
/// <see cref="CancelWorstCaseMs"/>: a minute and ten times the hard cap (660 s by default, 6,060 s
/// at most). A check whose resource counter does not move (non-linear real arithmetic can burn its
/// fuel so) is the case that waits that long.
/// </para>
/// </summary>
internal static class SmtPackageRunner
{
    #region Constants

    private const int SHOWN_VERSION_CHARACTERS = 32;

    #endregion

    #region Functions

    /// <summary>
    /// Checks a package's reference before anything is downloaded.
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <exception cref="SmtPackageException">This node does not take the package; the message is fixed words.</exception>
    public static void EnsureAcceptable(SmtTaskPackageData package)
    {
        // One controller version a job: a node on another one would run other code on the same
        // problems, and its rows could not be compared with anyone's.
        if (!string.Equals(package.RequiredControllerVersion, ControllerBuildInfo.VERSION, StringComparison.Ordinal))
            throw new SmtPackageException(SmtNodeFaults.VERSION_MISMATCH, $"this node runs Smt {ControllerBuildInfo.VERSION}; the package was made for {ShownVersion(package.RequiredControllerVersion)}");

        if (!SmtTickets.IsWellFormed(package.Ticket))
            throw new SmtPackageException(SmtNodeFaults.TICKET_INVALID, "the package has no ticket in the form of one");

        if (package.ProblemsBlobId == Guid.Empty)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_INVALID, "the package names no blob of problems");

        if (package.Options == null)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_INVALID, "the package carries no options");

        if (package.ProblemCount < 1 || package.ProblemCount > SmtPackageCodec.MAX_PACKAGE_PROBLEMS)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_INVALID, $"the package names {package.ProblemCount} problems; a node takes from 1 to {SmtPackageCodec.MAX_PACKAGE_PROBLEMS}");

        if (package.SizeBytes < 1)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_INVALID, "the package names no size of its blob");

        if (package.SizeBytes > SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_TOO_LARGE, $"the package's blob has {package.SizeBytes} bytes; a node takes at most {SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES}");
    }

    /// <summary>
    /// Checks a downloaded blob against its reference: its size, its SHA-256, and that it holds
    /// exactly the problems the reference names (their number and the digest of their indices).
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <param name="path">The blob's local file.</param>
    /// <exception cref="SmtPackageException">The blob is not the package its reference names, or larger than a node takes.</exception>
    /// <exception cref="IOException">The file cannot be read: a fault of the node's own.</exception>
    public static void EnsureBlob(SmtTaskPackageData package, string path)
    {
        var size = new FileInfo(path).Length;
        if (size > SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_TOO_LARGE, $"the package's blob has {size} bytes; a node takes at most {SmtPackageCodec.MAX_PROBLEMS_BLOB_BYTES}");

        if (size != package.SizeBytes)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_HASH_MISMATCH, $"the package's blob has {size} bytes, its reference names {package.SizeBytes}");

        if (!string.Equals(SmtPackageCodec.HashFile(path), package.Sha256, StringComparison.Ordinal))
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_HASH_MISMATCH, "the package's blob is not the one its reference names (the SHA-256 differs)");

        SmtTaskPackageReader.Check(path, package);
    }

    /// <summary>
    /// What this node runs a package with: its sandbox, and the number of sessions at once its
    /// cores and its memory allow under the package's options.
    /// </summary>
    /// <param name="package">The reference.</param>
    /// <param name="unavailable">Why this process has no sandbox, in fixed words; null when it has one.</param>
    /// <param name="degree">The degree for a session's memory (<see cref="SmtSandboxDegree.ForCurrentMachine"/> on a node).</param>
    /// <returns>The degree, at least 1.</returns>
    /// <exception cref="SmtPackageException">This process has no sandbox, or not one session fits in its memory.</exception>
    public static SmtDegree Prepare(SmtTaskPackageData package, string? unavailable, Func<long, SmtDegree> degree)
    {
        if (unavailable != null)
            throw new SmtPackageException(SmtNodeFaults.SANDBOX_UNAVAILABLE, unavailable);

        var session = SmtSandboxDegree.SessionBytes(package.Options!);
        var decided = degree(session);
        if (decided.Degree < 1)
        {
            throw new SmtPackageException(SmtNodeFaults.MEMORY_INSUFFICIENT,
                $"one session under the package's options may take {session} bytes; this machine has {decided.UsableBytes} bytes for sessions");
        }

        return decided;
    }

    /// <summary>
    /// Solves the problems of a checked package with a pool of sessions and writes a row of each
    /// to a package of results, as its session ends.
    /// </summary>
    /// <param name="package">The reference: its options, its ticket.</param>
    /// <param name="problemsPath">The package's blob, checked (<see cref="EnsureBlob"/>).</param>
    /// <param name="sandbox">The sandbox the sessions run in.</param>
    /// <param name="degree">The sessions at once.</param>
    /// <param name="resultsPath">The file the package of results is written to (made new).</param>
    /// <param name="problemDone">Called after each problem's row is written, on the thread that solved it.</param>
    /// <param name="logger">Receives a line for each problem that ended as a fault of the node.</param>
    /// <param name="cancellationToken">The job's token: no problem is handed out after it is cancelled, and the running sessions end at their next read.</param>
    /// <returns>The package file, complete, and how many rows it holds.</returns>
    /// <exception cref="OperationCanceledException">The job was cancelled; the file is deleted.</exception>
    /// <exception cref="SmtPackageException">The package's blob changed between its check and its reading.</exception>
    /// <exception cref="IOException">The node's own files failed.</exception>
    public static SmtWrittenResults Run(SmtTaskPackageData package, string problemsPath, SmtSandbox sandbox, int degree, string resultsPath,
        Action? problemDone, ILogger logger, CancellationToken cancellationToken)
    {
        var options = package.Options!;
        var gate = new object();

        try
        {
            using var reader = new SmtTaskPackageReader(problemsPath, package.ProblemCount);
            using var writer = new SmtResultPackageWriter(resultsPath);

            SmtWorkerPool.Run(package.ProblemCount, degree, _ =>
            {
                SmtProblemData? problem;
                lock (gate)
                {
                    if (!reader.TryRead(out problem))
                        throw new SmtPackageException(SmtNodeFaults.PACKAGE_MISMATCH, "the package's blob ended before the problems its reference names");
                }

                var outcome = SmtSolver.Solve(problem!, options, sandbox, null, cancellationToken);
                if (outcome.Status == SmtSolveStatus.NodeFault && outcome.Code != SmtSolveCodes.CANCELLED)
                    logger.LogWarning("Smt: problem {Index} has no answer on this node: {Code}", problem!.Index, outcome.Code);

                writer.Write(SmtResultRows.Of(problem!, outcome, package.Ticket));
                problemDone?.Invoke();
            }, cancellationToken);

            writer.Complete();
            return new SmtWrittenResults(resultsPath, writer.Rows);
        }
        catch
        {
            TryDelete(resultsPath);
            throw;
        }
    }

    /// <summary>
    /// The longest a running session can go on after its package was cancelled: the wall-clock
    /// backstop of a session under the options, a minute and ten times the hard cap.
    /// </summary>
    /// <param name="options">The package's options.</param>
    /// <returns>Milliseconds.</returns>
    public static int CancelWorstCaseMs(SmtOptionsData options)
    {
        return SmtProfile.Envelope(options).BackstopMs;
    }

    /// <summary>Deletes a package file; best-effort.</summary>
    /// <param name="path">The file, or null.</param>
    public static void TryDelete(string? path)
    {
        try
        {
            if (path != null && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the node's work folder goes as a whole
        }
    }

    private static string ShownVersion(string? version)
    {
        if (string.IsNullOrEmpty(version))
            return "a controller that names no version";

        var shown = version.Length > SHOWN_VERSION_CHARACTERS ? version[..SHOWN_VERSION_CHARACTERS] : version;
        return "Smt " + string.Concat(shown.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+' ? character : '?'));
    }

    #endregion
}
