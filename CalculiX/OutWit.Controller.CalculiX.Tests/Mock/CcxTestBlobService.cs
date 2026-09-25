using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.CalculiX.Tests.Mock;

/// <summary>
/// A blob service over a test folder: every upload is a file there, and a
/// blob id nobody uploaded is a FileNotFoundException - the infrastructure
/// failure a node sees when a blob cannot be fetched.
/// </summary>
internal sealed class CcxTestBlobService : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_blobPaths = new();

    private readonly string m_storagePath;

    #endregion

    #region Constructors

    public CcxTestBlobService(string storagePath)
    {
        m_storagePath = storagePath;
        Directory.CreateDirectory(m_storagePath);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Stores a text as a blob, as an initiator's upload would.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="fileName">A name whose extension the stored file keeps.</param>
    /// <returns>The blob id.</returns>
    public Guid AddText(string text, string fileName = "file.txt")
    {
        return UploadBytesAsync(System.Text.Encoding.UTF8.GetBytes(text), fileName).GetAwaiter().GetResult();
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
}
