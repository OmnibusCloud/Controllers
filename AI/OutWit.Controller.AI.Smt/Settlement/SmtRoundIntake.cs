using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// Takes in what the nodes returned for one fan-out. A node's word is a claim: a result package
/// is taken only when
/// <list type="bullet">
/// <item>its ticket is one this round gave out;</item>
/// <item>the platform, when it told the host which package's task the result came back for
/// (<see cref="SmtResultBindings"/>), told it the package the ticket names: a result returned for
/// another package is no answer to either (a node that answered one of its packages twice, in
/// place of another);</item>
/// <item>its reference is one a node returns: the words of a fault only beside a fault code, and
/// then a code of the node's list, a sentence no longer than a node writes, and no blob (the
/// node's word that it could not work on the package, counted so); for an answer a SHA-256 that
/// is one, and no word of a fault;</item>
/// <item>its blob is the bytes its reference names and is a package of results of this version,
/// read frame by frame within the bounds of the codec;</item>
/// <item>it holds exactly the rows of the package it answers, each once, and every row is one a
/// session could have written of its problem (<see cref="SmtRowChecks"/>).</item>
/// </list>
/// Anything else is refused and counted by reason; the same blob returned twice counts once. A
/// refused package fails that package and nothing else: its problems are simply without an
/// answer from it, and none of its rows stays in the host's spool.
/// <para>
/// <b>Which package a result came back for.</b> A result the platform told the host of is bound:
/// it is counted under the package whose task it was returned for, taken or refused, whatever
/// ticket it names, so a package that a node robbed of its answer is a package that came back
/// without one. A result the platform told nothing of (an older Grid, or a script that fills the
/// collection another way) is unbound and counted under the ticket it names, as before. Every
/// row of a taken package says which of the two it came in (<see cref="SmtCopy.Bound"/>).
/// </para>
/// <para>
/// <b>Whose fault a failure is.</b> Whatever goes wrong while a node's blob is fetched, read and
/// judged, of whatever type, makes that package unreadable and never ends the run: a node chose
/// the blob and every byte of it. That includes a failure of the blob service for that blob,
/// which the host cannot tell from the node's doing: the service gives a file or throws, and
/// nothing in what it throws says whether the blob was never there or the store failed. What the
/// host reads and writes of its own is another matter: its package of problems
/// (<see cref="SmtTaskBounds"/>) and its spool. A failure there is the server's, is counted
/// against no package, and ends the activity, which can be run again.
/// </para>
/// </summary>
internal static class SmtRoundIntake
{
    #region Constants

    /// <summary>A result under a ticket this round did not give out.</summary>
    public const string UNKNOWN_TICKET = "unknown_ticket";

    /// <summary>The same blob returned again.</summary>
    public const string DUPLICATE = "duplicate";

    /// <summary>Not a package of results this version reads, not the bytes its reference names (a reference whose SHA-256 is none names no bytes), or damaged.</summary>
    public const string UNREADABLE = "unreadable";

    /// <summary>Does not hold exactly the rows of its package, holds a row no session could have written, or is a reference with words of a fault that no node writes.</summary>
    public const string CONTENT = "content";

    /// <summary>More results under one ticket than copies were asked for.</summary>
    public const string SURPLUS = "surplus";

    /// <summary>A result the platform says was returned for a task of another package than the one its ticket names.</summary>
    public const string OTHER_PACKAGE = "other_package";

    /// <summary>Every reason, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> REASONS = [UNKNOWN_TICKET, DUPLICATE, UNREADABLE, CONTENT, SURPLUS, OTHER_PACKAGE];

    #endregion

    #region Functions

    /// <summary>Reads and checks every result package of a round.</summary>
    /// <param name="name">The round's name.</param>
    /// <param name="issued">The packages of problems the round gave out.</param>
    /// <param name="returned">The references Grid.ForEach returned, in any order.</param>
    /// <param name="copiesAsked">How many answers of a package the round asked for at the most.</param>
    /// <param name="surplusIsSuspect">
    /// What to do with a ticket that has more results than were asked for: true keeps them and
    /// marks the ticket (its problems go to a dispute), false drops them all.
    /// </param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null when only the comparison is needed.</param>
    /// <param name="cancellation">The job's cancellation, looked at before every result package is read.</param>
    /// <returns>The round as taken.</returns>
    /// <exception cref="InvalidOperationException">A package of problems this host made cannot be read: the run cannot be settled.</exception>
    /// <exception cref="SmtSettlementBoundException">The rows taken pass what the spool holds of one run.</exception>
    /// <exception cref="IOException">The host's own spool could not be written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static async Task<SmtRound> ReadAsync(string name, IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned,
        int copiesAsked, bool surplusIsSuspect, IWitBlobService blobs, SmtSpool? spool, CancellationToken cancellation = default)
    {
        var round = new SmtRound { Name = name, Issued = issued };
        var units = new Dictionary<string, SmtTaskPackageData>(StringComparer.Ordinal);
        foreach (var unit in issued)
        {
            if (SmtTickets.IsWellFormed(unit.Ticket))
                units[unit.Ticket] = unit;
        }

        var seenBlobs = new HashSet<Guid>();
        SmtTaskBounds? bounds = null;

        // An order that follows from the references, never from the order they came back in; a
        // ticket's answers one after the other, so its problems are read once for all of them.
        var ordered = returned
            .OfType<SmtResultPackageData>()
            .OrderBy(reference => reference.Ticket ?? "", StringComparer.Ordinal)
            .ThenBy(reference => reference.FaultCode ?? "", StringComparer.Ordinal)
            .ThenBy(reference => reference.Sha256 ?? "", StringComparer.Ordinal)
            .ThenBy(reference => reference.ResultsBlobId);

        foreach (var reference in ordered)
        {
            cancellation.ThrowIfCancellationRequested();

            // The package whose task the result came back for: the platform's word when it gave
            // one, the result's own otherwise. The result counts for that package, taken or refused.
            var bound = SmtResultBindings.TicketOf(reference);
            var returnedFor = ReturnedFor(bound, reference.Ticket, units);
            if (returnedFor != null)
                round.Returned[returnedFor] = round.Returned.GetValueOrDefault(returnedFor) + 1;

            if (bound == null)
                round.UnboundResults++;
            else
                round.BoundResults++;

            // A text that is no ticket is none this round gave out, however long it is.
            if (!SmtTickets.IsWellFormed(reference.Ticket) || !units.TryGetValue(reference.Ticket, out var unit))
            {
                Reject(round, returnedFor, UNKNOWN_TICKET);
                continue;
            }

            // Returned for a task of another package (or told for two): no answer to the one it
            // names, and the one it came back for has none from that task.
            if (bound != null && !string.Equals(bound, unit.Ticket, StringComparison.Ordinal))
            {
                Reject(round, returnedFor, OTHER_PACKAGE);
                continue;
            }

            // The node's word that it could not work on the package: one of its codes, its sentence, and no blob.
            if (!string.IsNullOrEmpty(reference.FaultCode))
            {
                if (IsFault(reference))
                {
                    round.Faults[reference.FaultCode] = round.Faults.GetValueOrDefault(reference.FaultCode) + 1;
                    round.Fates[unit.Ticket] = reference.FaultCode;
                }
                else
                {
                    Reject(round, unit.Ticket, CONTENT);
                }

                continue;
            }

            // An answer carries no word of a fault: a node leaves both empty, and never without a text.
            if (reference.FaultCode == null || reference.FaultMessage is not { Length: 0 })
            {
                Reject(round, unit.Ticket, CONTENT);
                continue;
            }

            // A text that is no SHA-256 names no bytes: nothing is fetched for it.
            if (!SmtDigestKey.TryParse(reference.Sha256, out _))
            {
                Reject(round, unit.Ticket, UNREADABLE);
                continue;
            }

            // The same reference returned twice (a node that answers two of its packages with one
            // blob). Two blobs with the same bytes are not told apart from two honest machines whose
            // rows are equal to the byte, and both are taken.
            if (!seenBlobs.Add(reference.ResultsBlobId))
            {
                Reject(round, unit.Ticket, DUPLICATE);
                continue;
            }

            var path = await LocateAsync(reference, unit, blobs);
            if (path == null)
            {
                Reject(round, unit.Ticket, UNREADABLE);
                continue;
            }

            // The host's own blob: if it cannot be read the run cannot be settled at all.
            if (bounds == null || !string.Equals(bounds.Ticket, unit.Ticket, StringComparison.Ordinal))
                bounds = await SmtTaskBounds.LoadAsync(unit, blobs);

            var (accepted, reason) = ReadPackage(unit, reference, path, bounds, spool, bound != null);
            if (accepted == null)
            {
                Reject(round, unit.Ticket, reason!);
                continue;
            }

            round.Accepted.Add(accepted);
        }

        DropOrMarkSurplus(round, copiesAsked, surplusIsSuspect);

        foreach (var package in round.Accepted)
        {
            foreach (var copy in package.Rows)
            {
                if (!round.Copies.TryGetValue(copy.Index, out var copies))
                    round.Copies[copy.Index] = copies = [];

                copies.Add(copy);
                round.UnitOf[copy.Index] = package.Unit;
            }
        }

        foreach (var index in round.Copies.Keys.ToList())
            round.Copies[index] = round.Copies[index].OrderBy(copy => copy.Digest.High).ThenBy(copy => copy.Digest.Low).ToList();

        return round;
    }

    #endregion

    #region Tools

    /// <summary>
    /// The ticket of the package of this round a result came back for: the one the platform told
    /// for a bound result, the one it names for an unbound one; null when that is no package of
    /// this round (a result bound to another round's package, or under a ticket the round did not
    /// give out).
    /// </summary>
    private static string? ReturnedFor(string? bound, string? named, IReadOnlyDictionary<string, SmtTaskPackageData> units)
    {
        var ticket = bound ?? named;
        return ticket != null && units.ContainsKey(ticket) ? ticket : null;
    }

    /// <summary>
    /// Whether a reference with a fault code is the one a node returns for a package it could not
    /// work on: no blob and no hash of one, a code of the node's list, and a sentence that is text
    /// and no longer than a node cuts it to. Nobody reads the sentence; it is held to its form so
    /// that nothing a node would not have written is taken for a node's word.
    /// </summary>
    private static bool IsFault(SmtResultPackageData reference)
    {
        return reference.ResultsBlobId == Guid.Empty
               && SmtNodeFaults.ALL.Contains(reference.FaultCode, StringComparer.Ordinal)
               && reference.Sha256 is { Length: 0 }
               && reference.FaultMessage is { Length: <= SmtResultWords.MAX_MESSAGE_CHARACTERS } sentence
               && SmtRowTexts.IsText(sentence);
    }

    /// <summary>
    /// The local file of a result blob, when the blob is there, is no larger than a package of
    /// results of its package may be and is the bytes its reference names; null otherwise,
    /// whatever failed. A failure of the blob service is taken as the blob's here: it is asked for
    /// a blob a node named and stored, and nothing in what it throws tells the store's own
    /// failure from a blob that was never there.
    /// </summary>
    private static async Task<string?> LocateAsync(SmtResultPackageData reference, SmtTaskPackageData unit, IWitBlobService blobs)
    {
        try
        {
            if (reference.ResultsBlobId == Guid.Empty)
                return null;

            var path = await blobs.GetLocalPathAsync(reference.ResultsBlobId);

            return new FileInfo(path).Length <= SmtPackageCodec.MaxResultsImageBytes(unit.ProblemCount)
                   && string.Equals(SmtPackageCodec.HashFile(path), reference.Sha256, StringComparison.Ordinal)
                ? path
                : null;
        }
        catch (Exception exception) when (IsOfThePackage(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Takes one package of results in, or says why not. Its rows go to the spool as they are
    /// read, a row at a time, and stay there only when the whole package holds: a package that is
    /// refused, at whichever row, leaves the spool as long as it was before it. Its rows share one
    /// budget of the host's evaluator, the package's own (<see cref="SmtPackageBudget"/>).
    /// </summary>
    private static (SmtAcceptedPackage? Accepted, string? Reason) ReadPackage(SmtTaskPackageData unit, SmtResultPackageData reference, string path, SmtTaskBounds bounds, SmtSpool? spool,
        bool bound)
    {
        var copies = new List<SmtCopy>(unit.ProblemCount);
        var mark = spool?.Length ?? 0;

        var reason = ReadRows(unit, path, bounds, spool, copies, bound, new SmtPackageBudget());

        // Exactly the package's problems: none missing, none of another package, none twice.
        if (reason == null && (copies.Count != unit.ProblemCount || !string.Equals(SmtTickets.IndexDigest(copies.Select(copy => copy.Index)), unit.IndexDigest, StringComparison.Ordinal)))
            reason = CONTENT;

        if (reason == null)
            return (new SmtAcceptedPackage(unit, reference.Sha256, copies, bound), null);

        spool?.Rewind(mark);
        return (null, reason);
    }

    /// <summary>
    /// Reads the rows of a package, each judged before it is kept. What a node's bytes make the
    /// reading or the judging throw is that package's, whatever its type, and the run goes on
    /// without it. The spool is the host's own file and stands outside that: what it throws (its
    /// bound, a disk that is full) says nothing about the package and is not caught.
    /// </summary>
    /// <returns>Null when every row was taken; else the reason the package is refused for.</returns>
    private static string? ReadRows(SmtTaskPackageData unit, string path, SmtTaskBounds bounds, SmtSpool? spool, List<SmtCopy> copies, bool bound, SmtPackageBudget budget)
    {
        using var rows = SmtResultPackageReader.Read(path, unit.ProblemCount).GetEnumerator();

        while (true)
        {
            SmtResultData row;
            SmtDigestKey digest;

            try
            {
                if (!rows.MoveNext())
                    return null;

                row = rows.Current;
                if (!SmtRowChecks.Holds(row, unit.Ticket, bounds) || !SmtDigestKey.TryParse(row.Digest, out digest))
                    return CONTENT;
            }
            catch (Exception exception) when (IsOfThePackage(exception))
            {
                return UNREADABLE;
            }

            var (offset, length) = spool?.Append(row) ?? (-1, 0);
            copies.Add(new SmtCopy
            {
                Index = row.Index,
                Digest = digest,
                Status = Word(row.Status, SmtResultWords.STATUSES),
                Verdict = Word(row.Verdict, SmtResultWords.VERDICTS),
                Code = row.Status == SmtResultWords.NODE_FAULT ? row.Code : "",
                ResourceCount = row.ResourceCount,
                Fuel = row.Fuel,
                Ticket = unit.Ticket,
                Bound = bound,
                SpoolOffset = offset,
                SpoolLength = length,
                Budget = budget
            });
        }
    }

    /// <summary>The list's own instance of a word a row was checked to hold.</summary>
    private static string Word(string word, IReadOnlyList<string> words)
    {
        foreach (var known in words)
        {
            if (string.Equals(known, word, StringComparison.Ordinal))
                return known;
        }

        return word;
    }

    /// <summary>
    /// Whether an exception met while a node's blob is fetched, read or judged is taken as that
    /// package's fault: every one but the two that say the process itself is out of room, and a
    /// cancellation, which is the job's.
    /// </summary>
    private static bool IsOfThePackage(Exception exception)
    {
        return exception is not (OutOfMemoryException or InsufficientExecutionStackException or OperationCanceledException);
    }

    private static void DropOrMarkSurplus(SmtRound round, int copiesAsked, bool surplusIsSuspect)
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
                Reject(round, ticket, SURPLUS);
        }
    }

    private static void Reject(SmtRound round, string? ticket, string reason)
    {
        round.Rejected[reason] = round.Rejected.GetValueOrDefault(reason) + 1;
        if (ticket != null)
            round.Fates[ticket] = reason;
    }

    #endregion
}
