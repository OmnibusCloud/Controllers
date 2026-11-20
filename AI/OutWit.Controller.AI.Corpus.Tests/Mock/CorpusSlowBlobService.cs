using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A blob service whose uploads take a while and then go through: a slow uplink. Every upload
/// is counted; the first one's blob id is kept.
/// </summary>
internal sealed class CorpusSlowBlobService : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_inner;

    private readonly TimeSpan m_delay;

    private int m_uploads;

    #endregion

    #region Constructors

    public CorpusSlowBlobService(IWitBlobService inner, TimeSpan delay)
    {
        m_inner = inner;
        m_delay = delay;
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        return m_inner.GetLocalPathAsync(blobId);
    }

    public async Task<Guid> UploadFileAsync(string localFilePath)
    {
        var call = Interlocked.Increment(ref m_uploads);
        await Task.Delay(m_delay);
        var blobId = await m_inner.UploadFileAsync(localFilePath);
        if (call == 1)
            First = blobId;

        return blobId;
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return m_inner.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Properties

    /// <summary>The upload calls made.</summary>
    public int Uploads => m_uploads;

    /// <summary>The blob id the first upload gave.</summary>
    public Guid First { get; private set; }

    #endregion
}
