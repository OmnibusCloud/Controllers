using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>A blob service whose next downloads or uploads fail: the blob channel of a node during a server restart.</summary>
internal sealed class CorpusFlakyBlobService : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_inner;

    private int m_downloadFailuresLeft;

    private int m_uploadFailuresLeft;

    #endregion

    #region Constructors

    public CorpusFlakyBlobService(IWitBlobService inner, int downloadFailures = 0, int uploadFailures = 0)
    {
        m_inner = inner;
        m_downloadFailuresLeft = downloadFailures;
        m_uploadFailuresLeft = uploadFailures;
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        Downloads++;
        if (Interlocked.Decrement(ref m_downloadFailuresLeft) >= 0)
            throw new IOException("Failed to connect to blob channel");

        return m_inner.GetLocalPathAsync(blobId);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        Uploads++;
        if (Interlocked.Decrement(ref m_uploadFailuresLeft) >= 0)
            throw new IOException("Blob upload failed");

        return m_inner.UploadFileAsync(localFilePath);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return m_inner.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Properties

    public int Downloads { get; private set; }

    public int Uploads { get; private set; }

    #endregion
}
