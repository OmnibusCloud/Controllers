using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A blob store of files in a folder of the test's own, as the other controllers' rigs have it:
/// an upload copies the file in, a download names the stored file. It counts the blobs it holds,
/// so a test can say that a package uploaded nothing.
/// </summary>
internal sealed class SmtTestBlobService : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_blobPaths = new();
    private readonly string m_storagePath;

    #endregion

    #region Constructors

    /// <summary>Makes the store in a folder.</summary>
    /// <param name="storagePath">The folder; made when missing.</param>
    public SmtTestBlobService(string storagePath)
    {
        m_storagePath = storagePath;
        Directory.CreateDirectory(m_storagePath);
    }

    #endregion

    #region Functions

    /// <summary>Replaces the bytes of a stored blob: a blob that is not what its reference names.</summary>
    /// <param name="blobId">The blob.</param>
    /// <param name="bytes">Its new bytes.</param>
    public void Overwrite(Guid blobId, byte[] bytes)
    {
        File.WriteAllBytes(m_blobPaths[blobId], bytes);
    }

    #endregion

    #region IWitBlobService

    /// <inheritdoc />
    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        if (!m_blobPaths.TryGetValue(blobId, out var path))
            throw new FileNotFoundException("The blob is not in the test's blob store.");

        return Task.FromResult(path);
    }

    /// <inheritdoc />
    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        var blobId = Guid.NewGuid();
        var destinationPath = Path.Combine(m_storagePath, $"{blobId:N}{Path.GetExtension(localFilePath)}");
        File.Copy(localFilePath, destinationPath, overwrite: true);
        m_blobPaths[blobId] = destinationPath;
        return Task.FromResult(blobId);
    }

    /// <inheritdoc />
    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        var blobId = Guid.NewGuid();
        var destinationPath = Path.Combine(m_storagePath, $"{blobId:N}{Path.GetExtension(fileName)}");
        File.WriteAllBytes(destinationPath, data);
        m_blobPaths[blobId] = destinationPath;
        return Task.FromResult(blobId);
    }

    #endregion

    #region Properties

    /// <summary>The blobs the store holds.</summary>
    public int Count => m_blobPaths.Count;

    #endregion
}
