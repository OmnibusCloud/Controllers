using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Tests.Mock;

/// <summary>
/// A file-backed blob service for the session tests: what the node's blob
/// cache does, without a cloud - register a file, get its local path back by
/// id, uploads land in a folder. A file reference made here describes the
/// stored bytes the way an initiator's does, by size and SHA-256, since the
/// node refuses a blob that is not what its reference says.
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
        return AddBytes(Encoding.UTF8.GetBytes(text), extension);
    }

    /// <summary>
    /// Registers bytes as a blob, exactly as given.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="extension">Extension for the stored copy.</param>
    /// <returns>The blob id.</returns>
    public Guid AddBytes(byte[] bytes, string extension = ".bin")
    {
        var blobId = Guid.NewGuid();
        var path = Path.Combine(m_storagePath, $"{blobId:N}{extension}");
        File.WriteAllBytes(path, bytes);
        m_blobPaths[blobId] = path;
        return blobId;
    }

    /// <summary>
    /// Registers a case file's text as a blob and describes it as an
    /// initiator would: its UTF-8 bytes' size and SHA-256.
    /// </summary>
    /// <param name="relativePath">The file's path inside the case.</param>
    /// <param name="text">The file's text.</param>
    /// <param name="templated">Whether the file carries tokens.</param>
    /// <returns>The file reference.</returns>
    public FoamFileRefData AddFile(string relativePath, string text, bool templated = false)
    {
        return AddFile(relativePath, Encoding.UTF8.GetBytes(text), templated);
    }

    /// <summary>
    /// Registers a case file's bytes as a blob and describes it as an
    /// initiator would: by size and SHA-256.
    /// </summary>
    /// <param name="relativePath">The file's path inside the case.</param>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="templated">Whether the file carries tokens.</param>
    /// <returns>The file reference.</returns>
    public FoamFileRefData AddFile(string relativePath, byte[] bytes, bool templated = false)
    {
        return new FoamFileRefData
        {
            RelativePath = relativePath,
            BlobId = AddBytes(bytes),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Size = bytes.Length,
            Templated = templated
        };
    }

    public string GetStoredPath(Guid blobId)
    {
        return m_blobPaths[blobId];
    }

    #endregion
}
