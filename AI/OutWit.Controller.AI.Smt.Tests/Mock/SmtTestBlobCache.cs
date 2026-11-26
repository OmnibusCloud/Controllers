using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A node's blob channel with a cache of its own, over a blob store of the test's: a download
/// hands out a copy of the blob in the cache's folder, a new file each time, and the cache may
/// drop what it handed out, as a real cache drops a file under a node that is short of room. It
/// counts the downloads, so a test can say that a blob was fetched again.
/// </summary>
internal sealed class SmtTestBlobCache : IWitBlobService
{
    #region Fields

    private readonly IWitBlobService m_store;

    private readonly string m_folder;

    private readonly ConcurrentQueue<string> m_handed = new();

    private int m_fetches;

    private int m_dropped;

    #endregion

    #region Constructors

    /// <summary>Makes the cache over a store.</summary>
    /// <param name="store">The blob store behind the channel.</param>
    /// <param name="folder">The cache's folder; made when missing.</param>
    public SmtTestBlobCache(IWitBlobService store, string folder)
    {
        m_store = store;
        m_folder = folder;
        Directory.CreateDirectory(folder);
    }

    #endregion

    #region Functions

    /// <summary>Drops every file the cache has handed out so far.</summary>
    public void Drop()
    {
        while (m_handed.TryDequeue(out var path))
        {
            File.Delete(path);
            Interlocked.Increment(ref m_dropped);
        }
    }

    #endregion

    #region IWitBlobService

    /// <inheritdoc />
    public async Task<string> GetLocalPathAsync(Guid blobId)
    {
        var stored = await m_store.GetLocalPathAsync(blobId);
        var cached = Path.Combine(m_folder, $"{Guid.NewGuid():N}{Path.GetExtension(stored)}");
        File.Copy(stored, cached);

        Interlocked.Increment(ref m_fetches);
        m_handed.Enqueue(cached);
        return cached;
    }

    /// <inheritdoc />
    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        return m_store.UploadFileAsync(localFilePath);
    }

    /// <inheritdoc />
    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return m_store.UploadBytesAsync(data, fileName);
    }

    #endregion

    #region Properties

    /// <summary>The downloads so far.</summary>
    public int Fetches => Volatile.Read(ref m_fetches);

    /// <summary>The files the cache dropped.</summary>
    public int Dropped => Volatile.Read(ref m_dropped);

    #endregion
}
