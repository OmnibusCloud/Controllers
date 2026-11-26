using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A host's temp storage in a folder of the test's own, as a contributor's client has one: every
/// scope is a folder under its root. It counts the scopes it made, shows the ones still there, and
/// can be told to refuse the next ones, as a full disk does.
/// </summary>
internal sealed class SmtTestTempStorage : IWitTempStorage
{
    #region Fields

    private int m_made;

    private int m_refusals;

    #endregion

    #region Constructors

    /// <summary>Makes the storage in a folder.</summary>
    /// <param name="rootPath">The folder; made when missing.</param>
    public SmtTestTempStorage(string rootPath)
    {
        RootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    #endregion

    #region Functions

    /// <summary>The next scopes asked for are refused with an <see cref="IOException"/>.</summary>
    /// <param name="scopes">How many.</param>
    public void RefuseNext(int scopes)
    {
        Volatile.Write(ref m_refusals, scopes);
    }

    #endregion

    #region IWitTempStorage

    /// <inheritdoc />
    public string CreateScope(string label)
    {
        if (Interlocked.Decrement(ref m_refusals) >= 0)
            throw new IOException("The test's temp storage refuses the scope.");

        Interlocked.Increment(ref m_made);
        var path = Path.Combine(RootPath, label, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public string CreateTempFile(string? extension = null)
    {
        return Path.Combine(RootPath, $"{Guid.NewGuid():N}{extension}");
    }

    /// <inheritdoc />
    public void DeleteScope(string scopePath)
    {
        if (Directory.Exists(scopePath))
            Directory.Delete(scopePath, recursive: true);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    #endregion

    #region Properties

    /// <inheritdoc />
    public string RootPath { get; }

    /// <summary>The scopes made so far.</summary>
    public int Made => Volatile.Read(ref m_made);

    /// <summary>The scopes that are still there: made, and not given back.</summary>
    public IReadOnlyList<string> Left => Directory.Exists(RootPath)
        ? Directory.GetDirectories(RootPath).SelectMany(Directory.GetDirectories).ToList()
        : [];

    #endregion
}
