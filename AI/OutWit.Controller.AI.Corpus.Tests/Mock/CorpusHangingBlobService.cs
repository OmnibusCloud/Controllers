using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A blob service whose first transfers never return: the call is taken and nothing ever comes
/// back, neither a path nor a failure. The blob channel's calls take no token, so nothing but
/// the caller's own time limit ends such a wait.
/// </summary>
internal sealed class CorpusHangingBlobService : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_inner;

    private int m_hangsLeft;

    private int m_hung;

    #endregion

    #region Constructors

    public CorpusHangingBlobService(IWitBlobService inner, int hangs)
    {
        m_inner = inner;
        m_hangsLeft = hangs;
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        return Interlocked.Decrement(ref m_hangsLeft) >= 0 ? Never<string>() : m_inner.GetLocalPathAsync(blobId);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        return Interlocked.Decrement(ref m_hangsLeft) >= 0 ? Never<Guid>() : m_inner.UploadFileAsync(localFilePath);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return m_inner.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Tools

    private Task<T> Never<T>()
    {
        Interlocked.Increment(ref m_hung);
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    #endregion

    #region Properties

    /// <summary>The calls that were taken and never answered.</summary>
    public int Hung => m_hung;

    #endregion
}
