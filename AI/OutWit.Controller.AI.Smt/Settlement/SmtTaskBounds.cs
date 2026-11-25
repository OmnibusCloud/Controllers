using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What a row of a package may say of its problem, read from the host's own package of problems:
/// the problem's id, the hash of its text, its query, the most models an enumeration of it may
/// find and whether a check of it asks for values; and the envelope every session of the package
/// ran in, made of the package's options as a node makes it (<see cref="SmtProfile.Envelope"/>).
/// A row that names a problem the package does not hold, or that says another id, hash or query
/// than the package gave, is no answer to it. The problems' texts are read and let go: what is
/// kept is a few words a problem.
/// </summary>
internal sealed class SmtTaskBounds
{
    #region Fields

    private readonly Dictionary<int, SmtTaskBound> m_bounds;

    #endregion

    #region Constructors

    private SmtTaskBounds(string ticket, SmtEnvelope envelope, Dictionary<int, SmtTaskBound> bounds)
    {
        Ticket = ticket;
        Envelope = envelope;
        m_bounds = bounds;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the bounds of a package of problems from its blob, a problem at a time.
    /// </summary>
    /// <param name="unit">The package's reference.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The bounds.</returns>
    /// <exception cref="InvalidOperationException">The host's own package cannot be read: the run cannot be settled.</exception>
    public static async Task<SmtTaskBounds> LoadAsync(SmtTaskPackageData unit, IWitBlobService blobs)
    {
        var path = await blobs.GetLocalPathAsync(unit.ProblemsBlobId);

        try
        {
            using var reader = new SmtTaskPackageReader(path, unit.ProblemCount);
            return Of(unit.Ticket, unit.Options, Problems(reader));
        }
        catch (SmtPackageException exception)
        {
            throw new InvalidOperationException($"A package of problems this host made cannot be read: {exception.Message}");
        }
    }

    /// <summary>
    /// Makes the bounds of a package of its problems and its options.
    /// </summary>
    /// <param name="ticket">The package's ticket.</param>
    /// <param name="options">The package's options, as its reference carries them; null for the defaults.</param>
    /// <param name="problems">The package's problems, each looked at once and let go.</param>
    /// <returns>The bounds.</returns>
    public static SmtTaskBounds Of(string ticket, SmtOptionsData? options, IEnumerable<SmtProblemData> problems)
    {
        var envelope = SmtProfile.Envelope(options ?? new SmtOptionsData());
        var bounds = new Dictionary<int, SmtTaskBound>();

        foreach (var problem in problems)
        {
            // The number of models as a node's plan takes it: the problem's own, or the options'.
            var models = problem.Query == SmtQuery.Enumerate ? problem.MaxModels > 0 ? problem.MaxModels : envelope.MaxModels : 0;
            var values = problem.Query == SmtQuery.Check && problem.Values is { Count: > 0 };
            bounds[problem.Index] = new SmtTaskBound(problem.Id, problem.Hash, problem.Query, models, values);
        }

        return new SmtTaskBounds(ticket, envelope, bounds);
    }

    /// <summary>The bound of a problem of the package.</summary>
    /// <param name="index">The problem's index.</param>
    /// <param name="bound">Its bound.</param>
    /// <returns>False when the package holds no such problem.</returns>
    public bool TryGet(int index, out SmtTaskBound bound)
    {
        return m_bounds.TryGetValue(index, out bound);
    }

    #endregion

    #region Tools

    private static IEnumerable<SmtProblemData> Problems(SmtTaskPackageReader reader)
    {
        while (reader.TryRead(out var problem))
            yield return problem!;
    }

    #endregion

    #region Properties

    /// <summary>The ticket of the package.</summary>
    public string Ticket { get; }

    /// <summary>The envelope a session of the package runs in: the ceilings of what a row may say it cost.</summary>
    public SmtEnvelope Envelope { get; }

    /// <summary>The indices of the package's problems.</summary>
    public IEnumerable<int> Indices => m_bounds.Keys;

    #endregion
}
