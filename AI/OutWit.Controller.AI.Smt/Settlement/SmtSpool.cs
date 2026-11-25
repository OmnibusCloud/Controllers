using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// A file the host keeps whole rows in while it settles a run: each is appended as it is read
/// from a node's package and read back alone when the evaluator or a document needs it. The rows
/// of a run, models and all, are never together in memory; the file is deleted when the settling
/// is done.
/// <para>
/// The rows of a package stay only when the whole package holds: whoever takes a package in notes
/// where the spool ended before it (<see cref="Length"/>) and cuts the spool back to there when
/// the package is refused (<see cref="Rewind"/>). And the file has a bound for a run
/// (<see cref="SmtSettlementBounds.MAX_ROWS_BYTES"/>): the row that would pass it is not written,
/// and the settlement fails by name.
/// </para>
/// </summary>
internal sealed class SmtSpool : IDisposable
{
    #region Fields

    private readonly string m_path;
    private readonly FileStream m_file;
    private readonly long m_maxBytes;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>A new spool in a folder.</summary>
    /// <param name="folder">The folder of the host's own working files; made when missing.</param>
    /// <param name="maxBytes">The most bytes of rows the spool holds.</param>
    public SmtSpool(string folder, long maxBytes = SmtSettlementBounds.MAX_ROWS_BYTES)
    {
        Directory.CreateDirectory(folder);
        m_path = Path.Combine(folder, $"smt-settle-{Guid.NewGuid():N}.spool");
        m_file = new FileStream(m_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        m_maxBytes = maxBytes;
    }

    #endregion

    #region Functions

    /// <summary>Appends a row.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Where it lies and how long it is.</returns>
    /// <exception cref="SmtSettlementBoundException">The row would take the spool past its bound; nothing of it was written.</exception>
    /// <exception cref="IOException">The host's own file could not be written.</exception>
    public (long Offset, int Length) Append(SmtResultData row)
    {
        var image = MemoryPackSerializer.Serialize(row);
        var offset = m_file.Length;
        if (offset + image.Length > m_maxBytes)
            throw new SmtSettlementBoundException(SmtSettlementBoundException.ROWS, m_maxBytes, "the rows the nodes returned");

        m_file.Seek(offset, SeekOrigin.Begin);
        m_file.Write(image);
        return (offset, image.Length);
    }

    /// <summary>
    /// Cuts the spool back to a length it had: the rows appended since are gone, as if they had
    /// never been written.
    /// </summary>
    /// <param name="length">The length to go back to, as <see cref="Length"/> gave it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The spool is shorter than that, or the length is below zero.</exception>
    public void Rewind(long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, m_file.Length);

        m_file.SetLength(length);
    }

    /// <summary>Reads a row back.</summary>
    /// <param name="copy">The copy whose row was kept.</param>
    /// <returns>The row.</returns>
    /// <exception cref="InvalidOperationException">The copy's row was not kept.</exception>
    public SmtResultData Read(SmtCopy copy)
    {
        if (copy.SpoolOffset < 0)
            throw new InvalidOperationException("The row of the copy was not kept.");

        var image = new byte[copy.SpoolLength];
        m_file.Seek(copy.SpoolOffset, SeekOrigin.Begin);
        m_file.ReadExactly(image);

        return MemoryPackSerializer.Deserialize<SmtResultData>(image)
               ?? throw new InvalidOperationException("The spool holds no row where one was kept.");
    }

    #endregion

    #region IDisposable

    /// <summary>Closes and deletes the file.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_file.Dispose();

        try
        {
            File.Delete(m_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a working file of the host's own; best-effort
        }
    }

    #endregion

    #region Properties

    /// <summary>The bytes of rows the spool holds: where the next row will lie.</summary>
    public long Length => m_file.Length;

    #endregion
}
