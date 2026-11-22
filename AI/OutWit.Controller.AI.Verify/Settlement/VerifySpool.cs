using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// A file the server keeps whole results in while it settles a run: each is appended as it is
/// read from a node's package and read back alone when it is the one a document needs. The
/// results of a run are never together in memory; the file is deleted when the settling is done.
/// </summary>
internal sealed class VerifySpool : IDisposable
{
    #region Fields

    private readonly string m_path;
    private readonly FileStream m_file;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// A new spool in a folder.
    /// </summary>
    /// <param name="folder">The folder of the server's own working files; made when missing.</param>
    public VerifySpool(string folder)
    {
        Directory.CreateDirectory(folder);
        m_path = Path.Combine(folder, $"verify-settle-{Guid.NewGuid():N}.spool");
        m_file = new FileStream(m_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    #endregion

    #region Functions

    /// <summary>Appends a result.</summary>
    /// <param name="result">The result.</param>
    /// <returns>Where it lies and how long it is.</returns>
    public (long Offset, int Length) Append(VerifyResultData result)
    {
        var image = MemoryPackSerializer.Serialize(result);
        m_file.Seek(0, SeekOrigin.End);
        var offset = m_file.Position;
        m_file.Write(image);
        return (offset, image.Length);
    }

    /// <summary>Reads a result back.</summary>
    /// <param name="execution">The execution whose result was kept.</param>
    /// <returns>The result.</returns>
    /// <exception cref="InvalidOperationException">The execution's result was not kept.</exception>
    public VerifyResultData Read(VerifyExecution execution)
    {
        if (execution.SpoolOffset < 0)
            throw new InvalidOperationException("The result of the execution was not kept.");

        var image = new byte[execution.SpoolLength];
        m_file.Seek(execution.SpoolOffset, SeekOrigin.Begin);
        m_file.ReadExactly(image);

        return MemoryPackSerializer.Deserialize<VerifyResultData>(image)
               ?? throw new InvalidOperationException("The spool holds no result where one was kept.");
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
            // a working file of the server's own; best-effort
        }
    }

    #endregion
}
