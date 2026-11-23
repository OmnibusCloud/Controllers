using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Planning;

/// <summary>
/// Stores the problems of a round as package blobs while they are given one at a time: a package
/// is written out as soon as the next problem starts another (<see cref="SmtPackageCutter"/>), so
/// the host holds the problems of one package, never of a run. Every reference carries a ticket of
/// its own, the controller version, the digest of the problems it holds and the run's options
/// (<see cref="SmtTaskPackages"/>).
/// </summary>
internal sealed class SmtPackagePublisher
{
    #region Fields

    private readonly SmtOptionsData m_options;
    private readonly IWitBlobService m_blobs;
    private readonly string m_folder;
    private readonly SmtPackageCutter m_cutter;
    private readonly List<SmtTaskPackageData> m_packages = [];
    private List<SmtProblemData> m_current = [];

    #endregion

    #region Constructors

    /// <summary>A publisher for the packages of a round.</summary>
    /// <param name="problems">The problems the round sends: what decides how many a package holds.</param>
    /// <param name="options">The run's options, resolved: every package carries them.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="folder">A folder of the host's own for the package files, each deleted once stored.</param>
    public SmtPackagePublisher(long problems, SmtOptionsData options, IWitBlobService blobs, string folder)
    {
        m_options = options;
        m_blobs = blobs;
        m_folder = folder;
        m_cutter = new SmtPackageCutter(SmtPackagePlanner.ProblemsPerPackage(problems));
    }

    #endregion

    #region Functions

    /// <summary>Adds a problem; the package before it is stored when the problem starts another.</summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <returns>A task that ends when the problem is taken.</returns>
    public async Task AddAsync(SmtProblemData problem)
    {
        var characters = SmtPackagePlanner.Characters(problem);
        if (m_cutter.StartsNew(characters))
            await StoreAsync();

        m_current.Add(problem);
        m_cutter.Add(characters);
        Problems++;
    }

    /// <summary>Stores the last package.</summary>
    /// <returns>The references, one a package, in order.</returns>
    public async Task<List<SmtTaskPackageData>> CompleteAsync()
    {
        if (m_current.Count > 0)
            await StoreAsync();

        return m_packages;
    }

    private async Task StoreAsync()
    {
        var problems = m_current;
        m_current = [];
        m_cutter.Reset();

        var package = await SmtTaskPackages.PublishAsync(problems, m_options, m_blobs, m_folder);
        m_packages.Add(package);
        Bytes += package.SizeBytes;
    }

    #endregion

    #region Properties

    /// <summary>The problems taken so far.</summary>
    public long Problems { get; private set; }

    /// <summary>The bytes of the package blobs stored so far.</summary>
    public long Bytes { get; private set; }

    /// <summary>The packages stored so far.</summary>
    public int Packages => m_packages.Count;

    #endregion
}
