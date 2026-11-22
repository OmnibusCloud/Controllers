using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>
/// A blob service that fails for the blobs a test names, with an exception of the test's
/// choosing: a store whose failure is of no type a reader was written to expect.
/// </summary>
internal sealed class VerifyFaultingBlobService : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_inner;
    private readonly HashSet<Guid> m_broken;
    private readonly Func<Exception> m_failure;

    #endregion

    #region Constructors

    public VerifyFaultingBlobService(IWitBlobService inner, IEnumerable<Guid> broken, Func<Exception> failure)
    {
        m_inner = inner;
        m_broken = broken.ToHashSet();
        m_failure = failure;
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        if (m_broken.Contains(blobId))
        {
            Failures++;
            throw m_failure();
        }

        return m_inner.GetLocalPathAsync(blobId);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        return m_inner.UploadFileAsync(localFilePath);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return m_inner.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Properties

    /// <summary>How often a named blob was asked for.</summary>
    public int Failures { get; private set; }

    #endregion
}
