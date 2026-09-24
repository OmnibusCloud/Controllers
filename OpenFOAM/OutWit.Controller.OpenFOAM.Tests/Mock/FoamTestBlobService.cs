using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Mock;

/// <summary>
/// A file-backed blob service for the session tests: what the node's blob
/// cache does, without a cloud - register a file, get its local path back by
/// id, uploads land in a folder.
/// </summary>
internal sealed class FoamTestBlobService : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_blobPaths = new();
    private readonly string m_storagePath;

    #endregion

    #region Constructors

    public FoamTestBlobService(string storagePath)
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

    /// <summary>
    /// Registers text as a blob, the way an initiator's upload would.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <param name="extension">Extension for the stored copy.</param>
    /// <returns>The blob id.</returns>
    public Guid AddText(string text, string extension = ".txt")
    {
        var blobId = Guid.NewGuid();
        var path = Path.Combine(m_storagePath, $"{blobId:N}{extension}");
        File.WriteAllText(path, text);
        m_blobPaths[blobId] = path;
        return blobId;
    }

    public string GetStoredPath(Guid blobId)
    {
        return m_blobPaths[blobId];
    }

    #endregion
}
