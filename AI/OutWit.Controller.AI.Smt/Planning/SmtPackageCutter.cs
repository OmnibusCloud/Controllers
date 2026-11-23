namespace OutWit.Controller.AI.Smt.Planning;

/// <summary>
/// The measure of the package being filled: how many problems it holds and how much text, and
/// whether the next problem starts another one (<see cref="SmtPackagePlanner"/>).
/// </summary>
internal sealed class SmtPackageCutter
{
    #region Fields

    private readonly int m_problemsPerPackage;

    #endregion

    #region Constructors

    /// <summary>A cutter for packages of at most a number of problems.</summary>
    /// <param name="problemsPerPackage">The most problems a package holds.</param>
    public SmtPackageCutter(int problemsPerPackage)
    {
        m_problemsPerPackage = Math.Max(1, problemsPerPackage);
    }

    #endregion

    #region Functions

    /// <summary>Whether the package must be closed before a problem of a size is added: it is full, or the problem's text would pass the package's. A problem alone always fits.</summary>
    /// <param name="characters">The problem's characters.</param>
    /// <returns>True when the problem starts another package.</returns>
    public bool StartsNew(long characters)
    {
        return Problems > 0 && (Problems >= m_problemsPerPackage || Characters + characters > SmtPackagePlanner.MAX_PACKAGE_CHARACTERS);
    }

    /// <summary>Counts a problem into the package.</summary>
    /// <param name="characters">The problem's characters.</param>
    public void Add(long characters)
    {
        Problems++;
        Characters += characters;
    }

    /// <summary>Starts an empty package.</summary>
    public void Reset()
    {
        Problems = 0;
        Characters = 0;
    }

    #endregion

    #region Properties

    /// <summary>The problems of the package so far.</summary>
    public int Problems { get; private set; }

    /// <summary>The characters of the package so far.</summary>
    public long Characters { get; private set; }

    #endregion
}
