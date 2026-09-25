using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Tests.Mock;

/// <summary>
/// The engine's default temp storage under a test folder, recording every
/// scope it hands out and every scope given back, so a test can tell that a
/// solve's scratch went back through the host's temp folder.
/// </summary>
internal sealed class RecordingTempStorage : IWitTempStorage
{
    #region Fields

    private readonly WitTempStorageDefault m_inner;

    private readonly ConcurrentQueue<string> m_createdScopes = new();

    private readonly ConcurrentQueue<string> m_deletedScopes = new();

    #endregion

    #region Constructors

    public RecordingTempStorage(string rootPath)
    {
        m_inner = new WitTempStorageDefault(rootPath);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Forgets what was recorded, for the next test of a shared fixture.
    /// </summary>
    public void Reset()
    {
        m_createdScopes.Clear();
        m_deletedScopes.Clear();
    }

    #endregion

    #region IWitTempStorage

    public string CreateScope(string label)
    {
        var scope = m_inner.CreateScope(label);
        m_createdScopes.Enqueue(scope);
        return scope;
    }

    public string CreateTempFile(string? extension = null)
    {
        return m_inner.CreateTempFile(extension);
    }

    public void DeleteScope(string scopePath)
    {
        m_deletedScopes.Enqueue(scopePath);
        m_inner.DeleteScope(scopePath);
    }

    public void DeleteFile(string path)
    {
        m_inner.DeleteFile(path);
    }

    public string RootPath => m_inner.RootPath;

    #endregion

    #region Properties

    /// <summary>Every scope handed out, in order.</summary>
    public IReadOnlyList<string> CreatedScopes => m_createdScopes.ToList();

    /// <summary>Every scope given back, in order.</summary>
    public IReadOnlyList<string> DeletedScopes => m_deletedScopes.ToList();

    #endregion
}
