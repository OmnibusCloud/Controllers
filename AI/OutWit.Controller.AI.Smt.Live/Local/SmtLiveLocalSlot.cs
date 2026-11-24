using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// One package of a local run in the pool (<see cref="SmtLiveLocalPool"/>): the file of its
/// problems, and the result package its rows are written to as their sessions end, completed
/// with its last row.
/// </summary>
internal sealed class SmtLiveLocalSlot : IDisposable
{
    #region Fields

    private SmtResultPackageWriter? m_writer;
    private int m_written;

    #endregion

    #region Constructors

    /// <summary>Makes the slot of a package.</summary>
    /// <param name="package">The package's reference.</param>
    /// <param name="problemsPath">The package's blob, checked.</param>
    /// <param name="resultsPath">The file its result package is written to.</param>
    public SmtLiveLocalSlot(SmtTaskPackageData package, string problemsPath, string resultsPath)
    {
        Package = package;
        ProblemsPath = problemsPath;
        ResultsPath = resultsPath;
    }

    #endregion

    #region Functions

    /// <summary>Opens the result package: once, under the pool's lock, when the package's first problem is handed out.</summary>
    public void Open()
    {
        m_writer = new SmtResultPackageWriter(ResultsPath);
    }

    /// <summary>Writes a row, from any thread; the result package is completed with the package's last row.</summary>
    /// <param name="row">The row.</param>
    /// <exception cref="InvalidOperationException">The result package is not open.</exception>
    public void Write(SmtResultData row)
    {
        var writer = m_writer ?? throw new InvalidOperationException("a row of a package whose result package is not open");
        writer.Write(row);

        if (Interlocked.Increment(ref m_written) != Package.ProblemCount)
            return;

        writer.Complete();
        Rows = writer.Rows;
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the result package's file.</summary>
    public void Dispose()
    {
        m_writer?.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The package's reference.</summary>
    public SmtTaskPackageData Package { get; }

    /// <summary>The package's blob.</summary>
    public string ProblemsPath { get; }

    /// <summary>The file of its result package.</summary>
    public string ResultsPath { get; }

    /// <summary>The rows of the completed result package; 0 until it is complete.</summary>
    public int Rows { get; private set; }

    #endregion
}
