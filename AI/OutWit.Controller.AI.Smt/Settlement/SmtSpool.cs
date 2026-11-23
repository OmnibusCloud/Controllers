using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// A file the host keeps whole rows in while it settles a run: each is appended as it is read
/// from a node's package and read back alone when the evaluator or a document needs it. The rows
/// of a run, models and all, are never together in memory; the file is deleted when the settling
/// is done.
/// </summary>
internal sealed class SmtSpool : IDisposable
{
    #region Fields

    private readonly string m_path;
    private readonly FileStream m_file;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>A new spool in a folder.</summary>
    /// <param name="folder">The folder of the host's own working files; made when missing.</param>
    public SmtSpool(string folder)
    {
        Directory.CreateDirectory(folder);
        m_path = Path.Combine(folder, $"smt-settle-{Guid.NewGuid():N}.spool");
        m_file = new FileStream(m_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    #endregion

    #region Functions

    /// <summary>Appends a row.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Where it lies and how long it is.</returns>
    public (long Offset, int Length) Append(SmtResultData row)
    {
        var image = MemoryPackSerializer.Serialize(row);
        m_file.Seek(0, SeekOrigin.End);
        var offset = m_file.Position;
        m_file.Write(image);
        return (offset, image.Length);
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
}
