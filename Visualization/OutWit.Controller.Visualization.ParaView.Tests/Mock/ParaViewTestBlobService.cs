using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Tests.Mock;

/// <summary>
/// File-backed blob service that ALSO accounts every local-path request: the distributed tests
/// assert from this log that no node materialized an attachment outside its task's subset.
/// </summary>
internal sealed class ParaViewTestBlobService : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_blobPaths = new();

    private readonly ConcurrentQueue<Guid> m_requests = new();

    private string m_storagePath;

    #endregion

    #region Constructors

    public ParaViewTestBlobService(string storagePath)
    {
        m_storagePath = storagePath;
        Directory.CreateDirectory(m_storagePath);
    }

    #endregion

    #region IWitBlobService

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        m_requests.Enqueue(blobId);

        if (!m_blobPaths.TryGetValue(blobId, out var path))
            throw new FileNotFoundException($"Blob '{blobId}' is not registered in the test blob service.");

        return Task.FromResult(path);
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        var blobId = Guid.NewGuid();
        var destinationPath = Path.Combine(m_storagePath, $"{blobId:N}{Path.GetExtension(localFilePath)}");
        File.Copy(localFilePath, destinationPath, overwrite: true);
        m_blobPaths[blobId] = destinationPath;
        return Task.FromResult(blobId);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        var blobId = Guid.NewGuid();
        var destinationPath = Path.Combine(m_storagePath, $"{blobId:N}{Path.GetExtension(fileName)}");
        File.WriteAllBytes(destinationPath, data);
        m_blobPaths[blobId] = destinationPath;
        return Task.FromResult(blobId);
    }

    #endregion

    #region Functions

    public void Reset(string storagePath)
    {
        m_storagePath = storagePath;
        Directory.CreateDirectory(m_storagePath);
        m_blobPaths.Clear();
        m_requests.Clear();
    }

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

    public IReadOnlyList<Guid> Requests => m_requests.ToArray();

    public void ClearRequests()
    {
        m_requests.Clear();
    }

    #endregion
}
