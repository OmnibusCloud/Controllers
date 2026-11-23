using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// Takes in what the nodes returned for one fan-out. A node's word is a claim: a result package
/// is taken only when
/// <list type="bullet">
/// <item>its ticket is one this round gave out;</item>
/// <item>it is a package of rows (a package with a fault code is the node's word that it could
/// not work on the package: one of the node's codes, without a blob, and counted so);</item>
/// <item>its blob is the bytes its reference names and is a package of results of this version,
/// read frame by frame within the bounds of the codec;</item>
/// <item>it holds exactly the rows of the package it answers, each once, and every row is one a
/// node may give of its problem (<see cref="SmtRowChecks"/>): its ticket, its problem's id, hash
/// and query, this controller version, the words and counts of a row, a digest that is its
/// own.</item>
/// </list>
/// Anything else is refused and counted by reason; the same blob returned twice counts once. A
/// refused package fails that package and nothing else: its problems are simply without an
/// answer from it. Whatever goes wrong while a package is read, of whatever type, makes that
/// package unreadable and never ends the run.
/// </summary>
internal static class SmtRoundIntake
{
    #region Constants

    /// <summary>A result under a ticket this round did not give out.</summary>
    public const string UNKNOWN_TICKET = "unknown_ticket";

    /// <summary>The same blob returned again.</summary>
    public const string DUPLICATE = "duplicate";

    /// <summary>Not a package of results this version reads, not the bytes its reference names, or damaged.</summary>
    public const string UNREADABLE = "unreadable";

    /// <summary>Does not hold exactly the rows of its package, or holds a row a node may not give.</summary>
    public const string CONTENT = "content";

    /// <summary>More results under one ticket than copies were asked for.</summary>
    public const string SURPLUS = "surplus";

    /// <summary>Every reason, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> REASONS = [UNKNOWN_TICKET, DUPLICATE, UNREADABLE, CONTENT, SURPLUS];

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
    /// <returns>The round as taken.</returns>
    public static async Task<SmtRound> ReadAsync(string name, IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned,
        int copiesAsked, bool surplusIsSuspect, IWitBlobService blobs, SmtSpool? spool)
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
            if (!units.TryGetValue(reference.Ticket ?? "", out var unit))
            {
                Reject(round, null, UNKNOWN_TICKET);
                continue;
            }

            round.Returned[unit.Ticket] = round.Returned.GetValueOrDefault(unit.Ticket) + 1;

            // The node's word that it could not work on the package: one of its codes, and no blob.
            if (!string.IsNullOrEmpty(reference.FaultCode))
            {
                if (reference.ResultsBlobId == Guid.Empty && SmtNodeFaults.ALL.Contains(reference.FaultCode, StringComparer.Ordinal))
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

            var (accepted, reason) = ReadPackage(unit, reference, path, bounds, spool);
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
    /// The local file of a result blob, when the blob is there, is no larger than a package of
    /// results of its package may be and is the bytes its reference names; null otherwise,
    /// whatever failed.
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

    private static (SmtAcceptedPackage? Accepted, string? Reason) ReadPackage(SmtTaskPackageData unit, SmtResultPackageData reference, string path, SmtTaskBounds bounds, SmtSpool? spool)
    {
        var copies = new List<SmtCopy>(unit.ProblemCount);

        try
        {
            foreach (var row in SmtResultPackageReader.Read(path, unit.ProblemCount))
            {
                if (!SmtRowChecks.Holds(row, unit.Ticket, bounds) || !SmtDigestKey.TryParse(row.Digest, out var digest))
                    return (null, CONTENT);

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
                    SpoolOffset = offset,
                    SpoolLength = length
                });
            }
        }
        catch (Exception exception) when (IsOfThePackage(exception))
        {
            // Not only the reader's own refusals: anything a node's bytes can make the reading
            // throw is that package's, and the run goes on without it.
            return (null, UNREADABLE);
        }

        // Exactly the package's problems: none missing, none of another package, none twice.
        if (copies.Count != unit.ProblemCount || !string.Equals(SmtTickets.IndexDigest(copies.Select(copy => copy.Index)), unit.IndexDigest, StringComparison.Ordinal))
            return (null, CONTENT);

        return (new SmtAcceptedPackage(unit, reference.Sha256, copies), null);
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
    /// Whether an exception met while a result package is read is taken as that package's fault:
    /// every one but the two that say the process itself is out of room.
    /// </summary>
    private static bool IsOfThePackage(Exception exception)
    {
        return exception is not (OutOfMemoryException or InsufficientExecutionStackException);
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
