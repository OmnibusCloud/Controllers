using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Live.Local;

/// <summary>
/// The blob service of a local run: files in a folder of the run's own. The host's and the node's
/// code store and fetch packages through it as they do through a server's blob store; here an
/// upload is a copy into the folder and a fetch names the stored file.
/// </summary>
internal sealed class SmtLiveBlobStore : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_paths = new();
    private readonly string m_folder;

    #endregion

    #region Constructors

    /// <summary>Makes the store in a folder.</summary>
    /// <param name="folder">The folder; made when it is not there.</param>
    public SmtLiveBlobStore(string folder)
    {
        m_folder = folder;
        Directory.CreateDirectory(m_folder);
    }

    #endregion

    #region IWitBlobService

    /// <inheritdoc />
    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        return m_paths.TryGetValue(blobId, out var path)
            ? Task.FromResult(path)
            : Task.FromException<string>(new FileNotFoundException($"the blob {blobId} is not in the local run's store"));
    }

    /// <inheritdoc />
    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        var blobId = Guid.NewGuid();
        var path = PathOf(blobId, localFilePath);
        File.Copy(localFilePath, path, overwrite: true);
        m_paths[blobId] = path;
        return Task.FromResult(blobId);
    }

    /// <inheritdoc />
    public async Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        var blobId = Guid.NewGuid();
        var path = PathOf(blobId, fileName);
        await File.WriteAllBytesAsync(path, data);
        m_paths[blobId] = path;
        return blobId;
    }

    #endregion

    #region Tools

    private string PathOf(Guid blobId, string name)
    {
        return Path.Combine(m_folder, $"{blobId:N}{Path.GetExtension(name)}");
    }

    #endregion

    #region Properties

    /// <summary>The blobs stored.</summary>
    public int Count => m_paths.Count;

    #endregion
}
