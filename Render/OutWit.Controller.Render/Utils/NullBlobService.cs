using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Utils;

/// <summary>
/// No-op implementation of <see cref="IWitBlobService"/>.
/// Registered as fallback when no real blob service is available (e.g., standalone WitEngine tests).
/// All operations throw <see cref="InvalidOperationException"/> at runtime.
/// </summary>
internal sealed class NullBlobService : IWitBlobService
{
    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        throw new InvalidOperationException(
            "Blob storage is not configured. The Render controller requires IWitBlobService to be registered by the host.");
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        throw new InvalidOperationException(
            "Blob storage is not configured. The Render controller requires IWitBlobService to be registered by the host.");
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        throw new InvalidOperationException(
            "Blob storage is not configured. The Render controller requires IWitBlobService to be registered by the host.");
    }
}
