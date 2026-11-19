using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// A round of a job run through the platform: the task packages the server wrote and the result
/// packages the nodes returned, all of them blobs, read one task package at a time.
/// <list type="bullet">
/// <item>An answer is bound to a task package by the package's ticket, before anything of it is
/// opened: a value that is no answer, or an answer under no ticket of this round, is dropped
/// unopened and counted.</item>
/// <item>Of the answers under one ticket only one more than the round asks for is opened
/// (<see cref="CorpusRoundSource.OpenedOf"/>); the rest are dropped unopened.</item>
/// <item>An opened answer is read frame by frame within the bounds of
/// <see cref="CorpusPackageCodec"/>, and gives each shard of its package one value at most: a
/// result under a ticket that is no shard's of the package is dropped, and when an answer holds
/// two results under one shard's ticket none of them is taken. So of the values under a shard's
/// ticket no two come from one answer, and the platform keeps one answer for each task of a
/// node's batch: that is what lets the ledger count values as machines. Whatever fails while an
/// answer is read (a frame that is no result, a stream that is none, a file that will not open)
/// costs that answer, under the reason <see cref="CorpusLedger.REJECTED_UNREADABLE"/>, and never
/// the run.</item>
/// <item>An answer that carries a fault code instead of a blob returns every shard of its package
/// as a fault of that node.</item>
/// </list>
/// The source holds the references and the local paths of the blobs and nothing of their
/// contents: at most <see cref="CorpusPackagePlanner.MAX_PACKAGES"/> packages a round, whatever
/// the manifest's length.
/// </summary>
public sealed class CorpusRoundSourceBlobs : CorpusRoundSource
{
    #region Constants

    /// <summary>What a host-side activity says when the task packages it was given are not the ones this controller wrote for the job.</summary>
    public const string ERROR_NOT_TASK_PACKAGES = "The round's task packages are not the ones Corpus wrote for this job.";

    #endregion

    #region Fields

    private readonly List<Package> m_packages;
    private readonly SortedDictionary<string, int> m_unbound;

    #endregion

    #region Constructors

    private CorpusRoundSourceBlobs(CorpusRoundKind kind, List<Package> packages, SortedDictionary<string, int> unbound)
        : base(kind)
    {
        m_packages = packages;
        m_unbound = unbound;

        Shards = packages.Sum(package => package.Reference.ShardCount);
        Missing = packages.Sum(package => Math.Max(0, Asked - package.Returned));
        Answers = CorpusRoundAnswers.Of(packages.Select(package => package.Returned));
    }

    #endregion

    #region Functions

    /// <summary>Binds a round's answers to its task packages and resolves the blobs that will be read.</summary>
    /// <param name="kind">Which round.</param>
    /// <param name="packages">The task packages the server made for the round, in the order it made them.</param>
    /// <param name="answers">What the nodes returned, in the order the platform handed it over; values of any kind.</param>
    /// <param name="blobs">Resolves a blob to a local file.</param>
    /// <returns>The source.</returns>
    /// <exception cref="InvalidOperationException">A task package reference is not one this controller writes.</exception>
    public static async Task<CorpusRoundSourceBlobs> OpenAsync(CorpusRoundKind kind, IEnumerable<object?> packages, IEnumerable<object?>? answers, IWitBlobService blobs)
    {
        var bound = new List<Package>();
        var tickets = new Dictionary<string, Package>(StringComparer.Ordinal);
        var unbound = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var value in packages)
        {
            if (value is not CorpusTaskPackageData reference
                || !CorpusTickets.IsWellFormed(reference.Ticket)
                || reference.ShardCount is < 1 or > CorpusPackagePlanner.MAX_PACKAGE_SHARDS
                || reference.TasksBlobId == Guid.Empty
                || bound.Count >= CorpusPackagePlanner.MAX_PACKAGES
                || !tickets.TryAdd(reference.Ticket, new Package(reference)))
                throw new InvalidOperationException(ERROR_NOT_TASK_PACKAGES);

            bound.Add(tickets[reference.Ticket]);
        }

        var opened = OpenedOf(kind);
        foreach (var value in answers ?? [])
        {
            if (value is not CorpusResultPackageData answer)
            {
                Count(unbound, CorpusLedger.REJECTED_UNREADABLE);
                continue;
            }

            if (!CorpusTickets.IsWellFormed(answer.Ticket) || !tickets.TryGetValue(answer.Ticket, out var package))
            {
                Count(unbound, CorpusLedger.REJECTED_UNKNOWN_TICKET);
                continue;
            }

            package.Returned++;
            if (package.Answers.Count < opened)
                package.Answers.Add(new Answer(answer));
        }

        foreach (var package in bound)
        {
            package.TasksPath = await blobs.GetLocalPathAsync(package.Reference.TasksBlobId);

            foreach (var answer in package.Answers)
                answer.Path = await ResolveAsync(blobs, answer.Reference);
        }

        return new CorpusRoundSourceBlobs(kind, bound, unbound);
    }

    /// <inheritdoc />
    public override IEnumerable<CorpusRoundPackage> Read()
    {
        var last = -1;

        foreach (var package in m_packages)
        {
            var tasks = ReadTasks(package);
            var slots = new List<CorpusRoundSlot>(tasks.Count);
            var tickets = new Dictionary<string, CorpusRoundSlot>(tasks.Count, StringComparer.Ordinal);

            foreach (var task in tasks)
            {
                // The server wrote the package: shards in ascending order, a ticket each.
                if (task.TaskIndex <= last || !CorpusTickets.IsWellFormed(task.Ticket))
                    throw new InvalidOperationException(ERROR_NOT_TASK_PACKAGES);

                last = task.TaskIndex;
                var slot = new CorpusRoundSlot(task);
                slots.Add(slot);
                if (!tickets.TryAdd(task.Ticket, slot))
                    throw new InvalidOperationException(ERROR_NOT_TASK_PACKAGES);
            }

            var dropped = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var answer in package.Answers)
                Take(answer, slots, tickets, dropped);

            for (var unopened = package.Returned - package.Answers.Count; unopened > 0; unopened--)
                Count(dropped, CorpusLedger.REJECTED_SURPLUS);

            yield return new CorpusRoundPackage(slots, package.Returned, dropped);
        }
    }

    #endregion

    #region Tools

    private static List<CorpusTaskData> ReadTasks(Package package)
    {
        List<CorpusTaskData> tasks;
        try
        {
            tasks = CorpusPackageCodec.ReadTasks(package.TasksPath, package.Reference.ShardCount);
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException(ERROR_NOT_TASK_PACKAGES);
        }

        if (tasks.Count != package.Reference.ShardCount)
            throw new InvalidOperationException(ERROR_NOT_TASK_PACKAGES);

        return tasks;
    }

    /// <summary>Gives the shards of a package what one answer holds for them: one value each at the most.</summary>
    private static void Take(Answer answer, List<CorpusRoundSlot> slots, Dictionary<string, CorpusRoundSlot> tickets, SortedDictionary<string, int> dropped)
    {
        var reference = answer.Reference;

        if (reference.ResultsBlobId == Guid.Empty && CorpusResultCodes.IsFault(reference.Fault))
        {
            foreach (var slot in slots)
                slot.Values.Add(CorpusRoundValue.Fault(reference.Fault));

            return;
        }

        // Neither an answer nor a fault, a blob that is not on the server, or one larger than the
        // results of the package can be: no shard of the package is answered by it.
        if (reference.ResultsBlobId == Guid.Empty || reference.Fault is not { Length: 0 } || answer.Path == null || !Fits(answer.Path, slots.Count))
        {
            foreach (var slot in slots)
                slot.Values.Add(CorpusRoundValue.Rejected(CorpusLedger.REJECTED_UNREADABLE));

            return;
        }

        var given = new Dictionary<CorpusRoundSlot, int>();
        try
        {
            foreach (var result in CorpusPackageCodec.ReadResults(answer.Path, slots.Count))
            {
                if (result == null)
                {
                    Count(dropped, CorpusLedger.REJECTED_UNREADABLE);
                    continue;
                }

                if (!CorpusTickets.IsWellFormed(result.Ticket) || !tickets.TryGetValue(result.Ticket, out var slot))
                {
                    Count(dropped, CorpusLedger.REJECTED_UNKNOWN_TICKET);
                    continue;
                }

                if (given.TryGetValue(slot, out var at))
                {
                    // Two results for one shard in one answer: which of them is the answer cannot be told.
                    slot.Values[at] = CorpusRoundValue.Rejected(CorpusLedger.REJECTED_SURPLUS);
                    Count(dropped, CorpusLedger.REJECTED_SURPLUS);
                    continue;
                }

                given[slot] = slot.Values.Count;
                slot.Values.Add(CorpusRoundValue.Of(result));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The rest of the answer cannot be read, whatever the reading said of it (a damaged
            // stream, a frame that is no result, a file that will not open): it costs this
            // answer and never the run. What was read before it stands.
            Count(dropped, CorpusLedger.REJECTED_UNREADABLE);
        }
    }

    /// <summary>Whether an answer's blob is a file of a size the results of its package can have; a file that cannot be asked is none.</summary>
    private static bool Fits(string path, int shards)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= CorpusPackageCodec.MaxResultsBlobBytes(shards);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static async Task<string?> ResolveAsync(IWitBlobService blobs, CorpusResultPackageData answer)
    {
        if (answer.ResultsBlobId == Guid.Empty)
            return null;

        try
        {
            return await blobs.GetLocalPathAsync(answer.ResultsBlobId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A blob the server does not hold: the answer names nothing.
            return null;
        }
    }

    private static void Count(IDictionary<string, int> counts, string reason)
    {
        counts[reason] = counts.TryGetValue(reason, out var count) ? count + 1 : 1;
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public override int Shards { get; }

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, int> Unbound => m_unbound;

    /// <inheritdoc />
    public override int Missing { get; }

    /// <inheritdoc />
    public override CorpusRoundAnswers Answers { get; }

    #endregion

    #region Types

    /// <summary>A task package of the round with the answers that will be opened for it.</summary>
    private sealed class Package(CorpusTaskPackageData reference)
    {
        public CorpusTaskPackageData Reference { get; } = reference;

        public string TasksPath { get; set; } = "";

        public List<Answer> Answers { get; } = [];

        public int Returned { get; set; }
    }

    /// <summary>One answer bound to a task package, and the local file of its blob when the server holds it.</summary>
    private sealed class Answer(CorpusResultPackageData reference)
    {
        public CorpusResultPackageData Reference { get; } = reference;

        public string? Path { get; set; }
    }

    #endregion
}
