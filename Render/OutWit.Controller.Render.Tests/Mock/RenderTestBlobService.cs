using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Tests.Mock;

internal sealed class RenderTestBlobService : IWitBlobService
{
    #region Fields

    private readonly Dictionary<Guid, string> m_blobPaths = new();
    private readonly string m_storagePath;

    #endregion

    #region Constructors

    public RenderTestBlobService(string storagePath)
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

    #region Functions

    public Guid RegisterExistingFile(string path)
    {
        var blobId = Guid.NewGuid();
        m_blobPaths[blobId] = path;
        return blobId;
    }

    public string GetStoredPath(Guid blobId)
    {
        return m_blobPaths[blobId];
    }

    #endregion
}
