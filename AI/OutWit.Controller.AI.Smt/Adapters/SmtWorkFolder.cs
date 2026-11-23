using Microsoft.Extensions.DependencyInjection;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// The folder of a call's working files: a scope of the host's temp storage when it has one (on a
/// contributor's machine, the client's own folder), a folder of its own in the operating system's
/// temp path otherwise. Made when the call starts and removed, with whatever is left in it, when
/// the call ends: a scope is the caller's to delete, and one that is only ever made stays on the
/// disk for good.
/// </summary>
internal sealed class SmtWorkFolder : IDisposable
{
    #region Constants

    private const string FALLBACK_FOLDER = "witai_smt_work";

    #endregion

    #region Fields

    private readonly IWitTempStorage? m_storage;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>Makes the folder.</summary>
    /// <param name="services">The host's services, or null.</param>
    /// <param name="label">The scope's label.</param>
    public SmtWorkFolder(IServiceProvider? services, string label)
    {
        m_storage = services?.GetService<IWitTempStorage>();
        Path = m_storage != null
            ? m_storage.CreateScope(label)
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), FALLBACK_FOLDER, Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    #endregion

    #region IDisposable

    /// <summary>Removes the folder; best-effort.</summary>
    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        try
        {
            if (m_storage != null)
                m_storage.DeleteScope(Path);
            else if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a working folder of the node's own
        }
    }

    #endregion

    #region Properties

    /// <summary>The folder's path.</summary>
    public string Path { get; }

    #endregion
}
