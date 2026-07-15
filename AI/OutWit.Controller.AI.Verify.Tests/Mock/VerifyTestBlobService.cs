using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Mock;

/// <summary>File-backed in-process blob store — the same mock shape the other controllers' skeleton tests use.</summary>
internal sealed class VerifyTestBlobService : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_blobPaths = new();
    private readonly string m_storagePath;

    #endregion

    #region Constructors

    public VerifyTestBlobService(string storagePath)
    {
        m_storagePath = storagePath;
        Directory.CreateDirectory(m_storagePath);
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        if (!m_blobPaths.TryGetValue(blobId, out var path))
            throw new FileNotFoundException($"Blob '{blobId}' is not registered in the test blob service.");

        return Task.FromResult(path);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        var blobId = Guid.NewGuid();
        var extension = Path.GetExtension(localFilePath);
        var destinationPath = Path.Combine(m_storagePath, $"{blobId:N}{extension}");
        File.Copy(localFilePath, destinationPath, overwrite: true);
        m_blobPaths[blobId] = destinationPath;
        return Task.FromResult(blobId);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        var blobId = Guid.NewGuid();
        var extension = Path.GetExtension(fileName);
        var destinationPath = Path.Combine(m_storagePath, $"{blobId:N}{extension}");
        File.WriteAllBytes(destinationPath, data);
        m_blobPaths[blobId] = destinationPath;
        return Task.FromResult(blobId);
    }

    #endregion
}
