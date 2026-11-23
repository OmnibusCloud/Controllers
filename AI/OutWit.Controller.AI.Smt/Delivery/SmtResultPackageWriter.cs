using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// Writes a package of results on a node as its problems are solved: straight into a file of the
/// node's own folder, one frame a row (<see cref="SmtPackageCodec"/>), in the order the sessions
/// end, so that the rows of a package, models and all, are never in memory together. The sessions
/// of a package write from several threads; a row is written whole before the next one starts.
/// </summary>
internal sealed class SmtResultPackageWriter : IDisposable
{
    #region Fields

    private readonly object m_gate = new();
    private readonly FileStream m_file;
    private readonly BrotliStream m_brotli;
    private bool m_completed;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Starts a package in a file (made new).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="IOException">The file cannot be made.</exception>
    public SmtResultPackageWriter(string path)
    {
        Path = path;
        m_file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        SmtPackageCodec.WriteHeader(m_file, SmtPackageCodec.KIND_RESULTS);
        m_brotli = SmtPackageCodec.Compressing(m_file);
    }

    #endregion

    #region Functions

    /// <summary>Appends one row; safe to call from several threads.</summary>
    /// <param name="row">The row.</param>
    /// <exception cref="ArgumentNullException">There is no row.</exception>
    /// <exception cref="InvalidOperationException">The package is complete.</exception>
    public void Write(SmtResultData row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Serialized outside the lock: a row with a model of megabytes does not hold the others.
        var image = MemoryPackSerializer.Serialize(row);

        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_completed)
                throw new InvalidOperationException("the package is complete");

            SmtPackageCodec.WriteFrame(m_brotli, image);
            Rows++;
        }
    }

    /// <summary>Ends the stream and closes the file: the package is whole.</summary>
    public void Complete()
    {
        lock (m_gate)
        {
            if (m_completed)
                return;

            m_completed = true;
            m_brotli.Dispose();
            m_file.Dispose();
        }
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

    /// <summary>The rows written.</summary>
    public int Rows { get; private set; }

    #endregion
}
