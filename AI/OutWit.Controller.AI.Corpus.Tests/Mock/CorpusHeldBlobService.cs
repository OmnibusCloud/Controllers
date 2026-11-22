using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A blob service whose uploads are held until the test lets them go: a call that is taken and
/// does not come back for as long as the test wants. Downloads pass through.
/// </summary>
internal sealed class CorpusHeldBlobService : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_inner;

    private readonly TaskCompletionSource m_released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int m_uploads;

    #endregion

    #region Constructors

    public CorpusHeldBlobService(IWitBlobService inner)
    {
        m_inner = inner;
    }

    #endregion

    #region Functions

    /// <summary>Lets every held upload, and every later one, go through.</summary>
    public void Release()
    {
        m_released.TrySetResult();
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        return m_inner.GetLocalPathAsync(blobId);
    }

    public async Task<Guid> UploadFileAsync(string localFilePath)
    {
        Interlocked.Increment(ref m_uploads);
        await m_released.Task;
        return await m_inner.UploadFileAsync(localFilePath);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return m_inner.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Properties

    /// <summary>The upload calls taken.</summary>
    public int Uploads => m_uploads;

    #endregion
}
