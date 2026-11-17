using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>A blob service whose every transfer fails with a message that names a folder of the node.</summary>
internal sealed class VerifyThrowingBlobService : IWitBlobService
{
    #region Fields

    private readonly string m_message;

    #endregion

    #region Constructors

    public VerifyThrowingBlobService(string message)
    {
        m_message = message;
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        Attempts++;
        throw new UnauthorizedAccessException(m_message);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        Attempts++;
        throw new UnauthorizedAccessException(m_message);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        Attempts++;
        throw new UnauthorizedAccessException(m_message);
    }

    #endregion

    #region Properties

    public int Attempts { get; private set; }

    #endregion
}
