using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// Takes in what the nodes returned for one fan-out. A node's word is a claim: a result package
/// is accepted only when
/// <list type="bullet">
/// <item>its ticket is one this round gave out;</item>
/// <item>its blob is the bytes its reference names, is a result package of this version, and is
/// no larger than one may be (read frame by frame, each within a bound);</item>
/// <item>every batch in it names the same ticket, the server's own controller version and
/// engine, and the runtime bytes the server pins;</item>
/// <item>it holds exactly the tasks of the package it answers, each once, and every result is
/// one a node may give (a verdict, or a fault of the node with one of its codes) and one a node
/// may give of that task: its numbers within the task's limits, its stdout within the task's
/// cap, its case records those of the task's suite (<see cref="VerifyTaskBounds"/>).</item>
/// </list>
/// Anything else is dropped and counted by reason; the same blob returned twice counts once.
/// A package that is dropped fails that package and nothing else: its tasks are simply without
/// an answer from it. Whatever goes wrong while a package is read, of whatever type, makes that
/// package unreadable and never ends the run.
/// </summary>
internal static class VerifyRoundIntake
{
    #region Constants

    /// <summary>A result under a ticket this round did not give out.</summary>
    public const string UNKNOWN_TICKET = "unknown_ticket";

    /// <summary>The same blob returned again.</summary>
    public const string DUPLICATE = "duplicate";

    /// <summary>Not a result package this version reads, not the bytes its reference names, damaged, or larger than one may be.</summary>
    public const string UNREADABLE = "unreadable";

    /// <summary>Made by another controller version, engine or runtime than the server's.</summary>
    public const string VERSION = "version";

    /// <summary>Does not hold exactly the tasks of its package, or holds a result a node may not give.</summary>
    public const string CONTENT = "content";

    /// <summary>More results under one ticket than copies were asked for.</summary>
    public const string SURPLUS = "surplus";

    private const int MAX_PLATFORM_LENGTH = 24;

    private static readonly HashSet<string> PLATFORMS = new(StringComparer.Ordinal) { "windows", "linux", "macos", "unknown" };

    /// <summary>Every reason, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> REASONS = [UNKNOWN_TICKET, DUPLICATE, UNREADABLE, VERSION, CONTENT, SURPLUS];

    #endregion

    #region Functions

    /// <summary>
    /// Reads and checks every result package of a round.
    /// </summary>
    /// <param name="name">The round's name.</param>
    /// <param name="issued">The task packages the round gave out.</param>
    /// <param name="returned">The references Grid.ForEach returned, in any order.</param>
    /// <param name="copiesAsked">How many executions of a package the round asked for at the most.</param>
    /// <param name="surplusIsSuspect">
    /// What to do with a ticket that has more results than were asked for: true keeps them and
    /// marks the ticket (the first fan-out: its tasks go to a dispute), false drops them all.
    /// </param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole results are kept for the documents, or null when only the comparison is needed.</param>
    /// <returns>The round as accepted.</returns>
    public static async Task<VerifyRound> ReadAsync(string name, IReadOnlyList<VerifyTaskPackageData> issued, IEnumerable<VerifyResultPackageData> returned,
        int copiesAsked, bool surplusIsSuspect, IWitBlobService blobs, VerifySpool? spool)
    {
        var round = new VerifyRound { Name = name, Issued = issued };
        var units = new Dictionary<string, VerifyTaskPackageData>(StringComparer.Ordinal);
        foreach (var unit in issued)
        {
            if (VerifyTickets.IsWellFormed(unit.Ticket))
                units[unit.Ticket] = unit;
        }

        var seenBlobs = new HashSet<Guid>();

        // The references are taken a ticket at a time, so the tasks of one package are read once
        // for all its answers and let go before the next.
        VerifyTaskBounds? bounds = null;

        // An order that follows from the references, never from the order they came back in.
        var ordered = returned
            .OrderBy(reference => reference.Ticket, StringComparer.Ordinal)
            .ThenBy(reference => reference.Sha256, StringComparer.Ordinal)
            .ThenBy(reference => reference.ResultsBlobId);

        foreach (var reference in ordered)
        {
            if (!units.TryGetValue(reference.Ticket ?? "", out var unit))
            {
                Reject(round, UNKNOWN_TICKET);
                continue;
            }

            round.Returned[unit.Ticket] = round.Returned.GetValueOrDefault(unit.Ticket) + 1;

            // The same reference returned twice (a node that answers two of its packages with one
            // blob). Two blobs with the same bytes are not told apart from two honest machines
            // whose answers happen to be equal to the byte, and are both taken.
            if (!seenBlobs.Add(reference.ResultsBlobId))
            {
                Reject(round, DUPLICATE);
                continue;
            }

            var path = await LocateAsync(reference, blobs);
            if (path == null)
            {
                Reject(round, UNREADABLE);
                continue;
            }

            // The server's own blob: if it cannot be read the run cannot be settled at all.
            if (bounds == null || !string.Equals(bounds.Ticket, unit.Ticket, StringComparison.Ordinal))
                bounds = await VerifyTaskBounds.LoadAsync(unit, blobs);

            var (accepted, reason) = ReadPackage(unit, reference, path, bounds, spool);
            if (accepted == null)
            {
                Reject(round, reason!);
                continue;
            }

            round.Accepted.Add(accepted);
        }

        DropOrMarkSurplus(round, copiesAsked, surplusIsSuspect);

        foreach (var package in round.Accepted)
        {
            foreach (var execution in package.Batches.SelectMany(batch => batch.Executions))
            {
                if (!round.Executions.TryGetValue(execution.Index, out var executions))
                    round.Executions[execution.Index] = executions = [];

                executions.Add(execution);
                round.UnitOf[execution.Index] = package.Unit;
            }
        }

        foreach (var index in round.Executions.Keys.ToList())
            round.Executions[index] = VerifyComparable.Ordered(round.Executions[index]);

        return round;
    }

    /// <summary>
    /// The local file of a result blob, when the blob is there, is no larger than a result
    /// package may be and is the bytes its reference names; null otherwise, whatever failed.
    /// </summary>
    private static async Task<string?> LocateAsync(VerifyResultPackageData reference, IWitBlobService blobs)
    {
        try
        {
            if (reference.ResultsBlobId == Guid.Empty)
                return null;

            var path = await blobs.GetLocalPathAsync(reference.ResultsBlobId);

            return new FileInfo(path).Length <= VerifyResultPackageReader.MAX_IMAGE_BYTES
                   && string.Equals(VerifyPackageCodec.HashFile(path), reference.Sha256, StringComparison.Ordinal)
                ? path
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static (VerifyAcceptedPackage? Accepted, string? Reason) ReadPackage(VerifyTaskPackageData unit, VerifyResultPackageData reference, string path,
        VerifyTaskBounds bounds, VerifySpool? spool)
    {
        var batches = new List<VerifyAcceptedBatch>();
        var indices = new List<int>(unit.TaskCount);

        try
        {
            foreach (var batch in VerifyResultPackageReader.Read(path, unit.BatchCount, unit.TaskCount))
            {
                if (!string.Equals(batch.Ticket, unit.Ticket, StringComparison.Ordinal))
                    return (null, CONTENT);

                if (!IsOfThisVersion(batch.Diagnostics))
                    return (null, VERSION);

                var executions = new List<VerifyExecution>(batch.Results.Count);
                foreach (var result in batch.Results)
                {
                    if (!IsOneANodeMayGive(result))
                        return (null, CONTENT);

                    indices.Add(result.TaskIndex);

                    var (offset, length) = spool?.Append(result) ?? (-1, 0);
                    executions.Add(VerifyComparable.Record(result, offset, length));
                }

                batches.Add(new VerifyAcceptedBatch(batch.Diagnostics!, executions));
            }
        }
        catch (VerifyPackageContentException)
        {
            return (null, CONTENT);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return (null, UNREADABLE);
        }

        // Exactly the package's tasks: none missing, none of another package, none twice.
        if (indices.Count != unit.TaskCount || !string.Equals(VerifyTickets.IndexDigest(indices), unit.IndexDigest, StringComparison.Ordinal))
            return (null, CONTENT);

        return (new VerifyAcceptedPackage(unit, reference.Sha256, batches), null);
    }

    /// <summary>
    /// Whether an exception met while a result package is read is taken as that package's
    /// fault: every one but the two that say the process itself is out of room.
    /// </summary>
    private static bool IsOfThePackage(Exception exception)
    {
        return exception is not (OutOfMemoryException or InsufficientExecutionStackException);
    }

    /// <summary>
    /// Whether a batch was run by the controller, the engine and the runtime bytes the server
    /// has, and names its platform in the fixed form: nothing a node says of itself is free text.
    /// </summary>
    private static bool IsOfThisVersion(VerifyBatchDiagnosticsData? diagnostics)
    {
        if (diagnostics == null)
            return false;

        if (!string.Equals(diagnostics.ControllerVersion, ControllerBuildInfo.VERSION, StringComparison.Ordinal))
            return false;

        if (!string.Equals(diagnostics.WasmtimeVersion, VerifyEngineVersion.Pinned, StringComparison.Ordinal))
            return false;

        if (!IsPlatform(diagnostics.RuntimeTarget))
            return false;

        var runtime = VerifyRuntimeCatalog.Find(diagnostics.RuntimeId ?? "");
        return runtime != null
               && string.Equals(diagnostics.RuntimeId, runtime.Id, StringComparison.Ordinal)
               && string.Equals(diagnostics.RuntimeSha256, runtime.Sha256, StringComparison.Ordinal);
    }

    /// <summary>A platform as the controller names it: an operating system and an architecture, e.g. <c>linux-x64</c>.</summary>
    private static bool IsPlatform(string? target)
    {
        if (target == null || target.Length > MAX_PLATFORM_LENGTH)
            return false;

        var dash = target.IndexOf('-');
        if (dash <= 0 || dash == target.Length - 1)
            return false;

        return PLATFORMS.Contains(target[..dash]) && target[(dash + 1)..].All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9');
    }

    /// <summary>
    /// Whether a result is one a node may give: a verdict a run can end with, or a fault of the
    /// node with one of the node's codes, outputs no longer than the ceilings allow, and a
    /// verdict that is the one its own records give (<see cref="IsWhatItsCasesSay"/>).
    /// </summary>
    private static bool IsOneANodeMayGive(VerifyResultData result)
    {
        if (!Enum.IsDefined(result.Verdict) || result.Verdict is VerifyVerdict.Disputed or VerifyVerdict.RuntimeUnavailable)
            return false;

        var faulted = !string.IsNullOrEmpty(result.Fault);
        if (faulted != (result.Verdict == VerifyVerdict.NoResult))
            return false;

        if (faulted && result.Fault is not (VerifyFault.WALL_CLOCK or VerifyFault.NODE_FAULT))
            return false;

        if (result.Stdout == null || result.Stderr == null || result.CaseResults == null)
            return false;

        if (result.Stdout.Length > VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES || result.Stderr.Length > VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES)
            return false;

        long caseOutput = 0;
        foreach (var item in result.CaseResults)
        {
            if (item?.ActualStdout == null || !Enum.IsDefined(item.Verdict) || item.Verdict is VerifyVerdict.Disputed or VerifyVerdict.NoResult or VerifyVerdict.RuntimeUnavailable)
                return false;

            caseOutput += item.ActualStdout.Length;
        }

        return caseOutput <= VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES;
    }

    /// <summary>
    /// Whether a result's verdict and exit code are the ones its own records give, as a node
    /// makes them: a pass ended with exit code 0; a case passed exactly when its verdict is a
    /// pass; and a task with case records passed when all of them did, and otherwise has the
    /// verdict and the exit code of the first that did not.
    /// </summary>
    private static bool IsWhatItsCasesSay(VerifyResultData result)
    {
        if (result.Verdict == VerifyVerdict.Pass && result.ExitCode != 0)
            return false;

        VerifyCaseResultData? firstUnmet = null;
        foreach (var item in result.CaseResults)
        {
            if (item.Passed != (item.Verdict == VerifyVerdict.Pass))
                return false;

            if (!item.Passed)
                firstUnmet ??= item;
        }

        if (result.CaseResults.Count == 0)
            return true;

        return firstUnmet == null
            ? result.Verdict == VerifyVerdict.Pass
            : result.Verdict == firstUnmet.Verdict && result.ExitCode == firstUnmet.ExitCode;
    }

    private static void DropOrMarkSurplus(VerifyRound round, int copiesAsked, bool surplusIsSuspect)
    {
        var crowded = round.Accepted
            .GroupBy(package => package.Unit.Ticket, StringComparer.Ordinal)
            .Where(copies => copies.Count() > copiesAsked)
            .Select(copies => copies.Key)
            .ToList();

        foreach (var ticket in crowded)
        {
            if (surplusIsSuspect)
            {
                round.Surplus.Add(ticket);
                continue;
            }

            // Which of them is the answer cannot be told, so none is.
            var dropped = round.Accepted.RemoveAll(package => package.Unit.Ticket == ticket);
            for (var copy = 0; copy < dropped; copy++)
                Reject(round, SURPLUS);
        }
    }

    private static void Reject(VerifyRound round, string reason)
    {
        round.Rejected[reason] = round.Rejected.GetValueOrDefault(reason) + 1;
    }

    #endregion
}
