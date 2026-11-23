using MemoryPack;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Planning;

/// <summary>
/// A file the host keeps the taken problems of a set in while it reads the set: each is appended
/// as the reader takes it, and they are read back in order once the set is read and the run knows
/// how many it has (which decides how they are cut into packages). The problems of a set are never
/// together in memory; the file is deleted with the spool.
/// </summary>
internal sealed class SmtProblemSpool : IDisposable
{
    #region Fields

    private readonly string m_path;
    private readonly FileStream m_file;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>A new spool in a folder.</summary>
    /// <param name="folder">The folder of the host's own working files; made when missing.</param>
    public SmtProblemSpool(string folder)
    {
        Directory.CreateDirectory(folder);
        m_path = Path.Combine(folder, $"smt-split-{Guid.NewGuid():N}.spool");
        m_file = new FileStream(m_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    #endregion

    #region Functions

    /// <summary>Appends a problem.</summary>
    /// <param name="problem">The problem as it travels.</param>
    public void Append(SmtProblemData problem)
    {
        SmtPackageCodec.WriteFrame(m_file, MemoryPackSerializer.Serialize(problem));
        Count++;
    }

    /// <summary>Reads the problems back, in the order they were appended.</summary>
    /// <returns>The problems, each read when asked for.</returns>
    public IEnumerable<SmtProblemData> ReadAll()
    {
        m_file.Flush();
        m_file.Seek(0, SeekOrigin.Begin);

        while (SmtPackageCodec.ReadFrame(m_file, SmtPackageCodec.MAX_PROBLEM_FRAME_BYTES) is { } image)
            yield return MemoryPackSerializer.Deserialize<SmtProblemData>(image) ?? throw new InvalidDataException("The spool holds no problem where one was kept.");
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

    /// <summary>The problems appended.</summary>
    public int Count { get; private set; }

    #endregion
}
