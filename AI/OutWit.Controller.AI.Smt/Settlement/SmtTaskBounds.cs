using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What a row of a package may say of its problem, read from the host's own package of problems:
/// the problem's id, the hash of its text, its query and the most models an enumeration of it may
/// find. A row that names a problem the package does not hold, or that says another id, hash or
/// query than the package gave, is no answer to it. The problems' texts are read and let go: what
/// is kept is a few words a problem.
/// </summary>
internal sealed class SmtTaskBounds
{
    #region Fields

    private readonly Dictionary<int, SmtTaskBound> m_bounds;

    #endregion

    #region Constructors

    private SmtTaskBounds(string ticket, Dictionary<int, SmtTaskBound> bounds)
    {
        Ticket = ticket;
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
        var options = SmtOptionsRules.Resolve(unit.Options ?? new SmtOptionsData());
        var bounds = new Dictionary<int, SmtTaskBound>(unit.ProblemCount);

        try
        {
            using var reader = new SmtTaskPackageReader(path, unit.ProblemCount);
            while (reader.TryRead(out var problem))
            {
                var models = problem!.Query == SmtQuery.Enumerate ? problem.MaxModels > 0 ? problem.MaxModels : options.MaxModels : 0;
                bounds[problem.Index] = new SmtTaskBound(problem.Id, problem.Hash, problem.Query, models);
            }
        }
        catch (SmtPackageException exception)
        {
            throw new InvalidOperationException($"A package of problems this host made cannot be read: {exception.Message}");
        }

        return new SmtTaskBounds(unit.Ticket, bounds);
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

    #region Properties

    /// <summary>The ticket of the package.</summary>
    public string Ticket { get; }

    /// <summary>The indices of the package's problems.</summary>
    public IEnumerable<int> Indices => m_bounds.Keys;

    #endregion
}
