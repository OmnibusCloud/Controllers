using Microsoft.Extensions.DependencyInjection;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Adapters;

/// <summary>
/// The folder of the server's working files for one call of a host-side activity: a scope of
/// the host's temp storage when it has one, a folder of its own in the operating system's temp
/// path otherwise. Made when the call starts and removed, with whatever is left in it, when the
/// call ends: a scope is the caller's to delete, and one that is only ever made stays on the
/// disk for good.
/// </summary>
internal sealed class VerifyWorkFolder : IDisposable
{
    #region Constants

    private const string SCOPE_LABEL = "verify-settle";
    private const string FALLBACK_FOLDER = "witai_verify_settle";

    #endregion

    #region Fields

    private readonly IWitTempStorage? m_storage;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>Makes the folder.</summary>
    /// <param name="services">The host's services.</param>
    public VerifyWorkFolder(IServiceProvider services)
    {
        m_storage = services.GetService<IWitTempStorage>();
        Path = m_storage != null
            ? m_storage.CreateScope(SCOPE_LABEL)
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
            // a working folder of the server's own
        }
    }

    #endregion

    #region Properties

    /// <summary>The folder's path.</summary>
    public string Path { get; }

    #endregion
}
