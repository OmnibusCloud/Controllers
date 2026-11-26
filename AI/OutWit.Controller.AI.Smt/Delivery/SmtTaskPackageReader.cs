using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// Reads a package of problems on a node, one problem at a time, each frame held to its bound
/// before it is decoded (<see cref="SmtPackageCodec"/>). The host made the package, and still
/// nothing in it is believed: a blob that is not a package of problems, a damaged stream, a frame
/// longer than a problem may be, more bytes in all than a package may unpack to, or more problems
/// than the reader was told to expect is refused with the code of its kind
/// (<see cref="SmtPackageException"/>).
/// <para>
/// <see cref="Check"/> reads a whole package once, holding none of it, and says whether it holds
/// exactly the problems its reference names; the node then reads it a second time and hands the
/// problems out as its sessions take them, so the problems of a package are never all in memory.
/// </para>
/// </summary>
internal sealed class SmtTaskPackageReader : IDisposable
{
    #region Fields

    private readonly FileStream m_file;
    private readonly BrotliStream m_brotli;
    private readonly int m_maxProblems;
    private readonly long m_maxImageBytes;
    private long m_bytes;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Opens a package of problems and reads its header.
    /// </summary>
    /// <param name="path">The package's file.</param>
    /// <param name="maxProblems">The most problems the package may hold.</param>
    /// <param name="maxImageBytes">The most bytes the package may unpack to; 0 for <see cref="SmtPackageCodec.MAX_PROBLEMS_IMAGE_BYTES"/>.</param>
    /// <exception cref="SmtPackageException">The file is not a package of problems of this format (<see cref="SmtNodeFaults.PACKAGE_UNREADABLE"/>).</exception>
    /// <exception cref="IOException">The file cannot be opened.</exception>
    public SmtTaskPackageReader(string path, int maxProblems, long maxImageBytes = 0)
    {
        m_maxProblems = maxProblems;
        m_maxImageBytes = maxImageBytes > 0 ? maxImageBytes : SmtPackageCodec.MAX_PROBLEMS_IMAGE_BYTES;
        m_file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            SmtPackageCodec.ReadHeader(m_file, SmtPackageCodec.KIND_PROBLEMS);
        }
        catch (InvalidDataException exception)
        {
            m_file.Dispose();
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_UNREADABLE, exception.Message);
        }

        m_brotli = new BrotliStream(m_file, CompressionMode.Decompress);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the next problem.
    /// </summary>
    /// <param name="problem">The problem; null at the end of the package.</param>
    /// <returns>False at the end of the package.</returns>
    /// <exception cref="SmtPackageException">The package is damaged, too large, or holds more problems than it may.</exception>
    public bool TryRead([NotNullWhen(true)] out SmtProblemData? problem)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        problem = null;

        byte[]? image;
        try
        {
            image = SmtPackageCodec.ReadFrame(m_brotli, SmtPackageCodec.MAX_PROBLEM_FRAME_BYTES);
        }
        catch (InvalidDataException exception)
        {
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_UNREADABLE, exception.Message);
        }

        if (image == null)
            return false;

        m_bytes += SmtPackageCodec.FRAME_HEAD_LENGTH + image.Length;
        if (m_bytes > m_maxImageBytes)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_TOO_LARGE, $"the package unpacks to more than the {m_maxImageBytes} bytes a package may hold");

        if (Read >= m_maxProblems)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_MISMATCH, $"the package holds more than the {m_maxProblems} problems its reference names");

        problem = Decode(image);
        Read++;
        return true;
    }

    /// <summary>
    /// Reads a whole package once and holds it to its reference: exactly the number of problems
    /// it names, with exactly the indices its digest names. Nothing of it is kept.
    /// </summary>
    /// <param name="path">The package's file.</param>
    /// <param name="package">The reference.</param>
    /// <param name="maxImageBytes">The most bytes the package may unpack to; 0 for <see cref="SmtPackageCodec.MAX_PROBLEMS_IMAGE_BYTES"/>.</param>
    /// <exception cref="SmtPackageException">The package is not readable, too large, or not what its reference names.</exception>
    /// <exception cref="IOException">The file cannot be opened.</exception>
    public static void Check(string path, SmtTaskPackageData package, long maxImageBytes = 0)
    {
        using var reader = new SmtTaskPackageReader(path, package.ProblemCount, maxImageBytes);
        var indices = new List<int>(package.ProblemCount);
        while (reader.TryRead(out var problem))
            indices.Add(problem.Index);

        if (indices.Count != package.ProblemCount)
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_MISMATCH, $"the package holds {indices.Count} problems, its reference names {package.ProblemCount}");

        if (!string.Equals(SmtTickets.IndexDigest(indices), package.IndexDigest, StringComparison.Ordinal))
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_MISMATCH, "the package holds other problems than its reference names (the digest of their indices differs)");
    }

    /// <summary>
    /// Decodes a frame. Whatever the decoder throws, of any type, is a frame that cannot be read:
    /// it fails its package, never the caller.
    /// </summary>
    private static SmtProblemData Decode(byte[] image)
    {
        try
        {
            return MemoryPackSerializer.Deserialize<SmtProblemData>(image)
                   ?? throw new SmtPackageException(SmtNodeFaults.PACKAGE_UNREADABLE, "a frame of the package holds no problem");
        }
        catch (Exception exception) when (exception is not (SmtPackageException or OutOfMemoryException))
        {
            throw new SmtPackageException(SmtNodeFaults.PACKAGE_UNREADABLE, "a frame of the package cannot be read as a problem");
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Closes the file.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_brotli.Dispose();
        m_file.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>The problems read so far.</summary>
    public int Read { get; private set; }

    #endregion
}
