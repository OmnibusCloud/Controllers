using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// Writes a package of problems straight into a file, one frame a problem
/// (<see cref="SmtPackageCodec"/>), and keeps what the package's reference needs: how many
/// problems it holds and the digest of their indices. The problems of a package are never all in
/// memory at once.
/// </summary>
internal sealed class SmtTaskPackageWriter : IDisposable
{
    #region Fields

    private readonly FileStream m_file;
    private readonly BrotliStream m_brotli;
    private readonly List<int> m_indices = [];
    private bool m_completed;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Starts a package in a file (made new).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="IOException">The file cannot be made.</exception>
    public SmtTaskPackageWriter(string path)
    {
        Path = path;
        m_file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        SmtPackageCodec.WriteHeader(m_file, SmtPackageCodec.KIND_PROBLEMS);
        m_brotli = SmtPackageCodec.Compressing(m_file);
    }

    #endregion

    #region Functions

    /// <summary>Appends one problem.</summary>
    /// <param name="problem">The problem as it travels.</param>
    /// <exception cref="ArgumentNullException">There is no problem.</exception>
    /// <exception cref="InvalidOperationException">The package is complete.</exception>
    public void Write(SmtProblemData problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (m_completed)
            throw new InvalidOperationException("the package is complete");

        SmtPackageCodec.WriteFrame(m_brotli, MemoryPackSerializer.Serialize(problem));
        m_indices.Add(problem.Index);
    }

    /// <summary>Ends the stream and closes the file: the package is whole.</summary>
    public void Complete()
    {
        if (m_completed)
            return;

        m_completed = true;
        m_brotli.Dispose();
        m_file.Dispose();
        IndexDigest = SmtTickets.IndexDigest(m_indices);
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the file; a package that was not completed is not one.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        Complete();
        m_disposed = true;
    }

    #endregion

    #region Properties

    /// <summary>The file the package is written to.</summary>
    public string Path { get; }

    /// <summary>The problems written.</summary>
    public int Problems => m_indices.Count;

    /// <summary>The digest of the written problems' indices (<see cref="SmtTickets.IndexDigest"/>); empty until the package is complete.</summary>
    public string IndexDigest { get; private set; } = "";

    #endregion
}
