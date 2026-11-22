using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>A blob service whose next transfers fail: the link of a node that drops and comes back.</summary>
internal sealed class VerifyFlakyBlobService : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_inner;

    private int m_failuresLeft;

    #endregion

    #region Constructors

    public VerifyFlakyBlobService(IWitBlobService inner, int failures)
    {
        m_inner = inner;
        m_failuresLeft = failures;
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        FailWhileBroken();
        return m_inner.GetLocalPathAsync(blobId);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        FailWhileBroken();
        return m_inner.UploadFileAsync(localFilePath);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        FailWhileBroken();
        return m_inner.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Tools

    private void FailWhileBroken()
    {
        Attempts++;

        if (Interlocked.Decrement(ref m_failuresLeft) >= 0)
            throw new IOException("Blob transfer failed: the link is down.");
    }

    #endregion

    #region Properties

    public int Attempts { get; private set; }

    #endregion
}
