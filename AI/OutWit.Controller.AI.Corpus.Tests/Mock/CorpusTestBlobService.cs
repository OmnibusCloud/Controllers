using System.Collections.Concurrent;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>File-backed in-process blob store — the same mock shape the other controllers' skeleton tests use.</summary>
public sealed class CorpusTestBlobService : IWitBlobService
{
    #region Fields

    private readonly ConcurrentDictionary<Guid, string> m_blobPaths = new();
    private readonly string m_storagePath;

    #endregion

    #region Constructors

    public CorpusTestBlobService(string storagePath)
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

    #region Storage

    /// <summary>
    /// The member the server's blob service is proposed to carry (BlobStorage:JobStorageHeadroomBytes),
    /// read by the controller by reflection: the e2e rig states a headroom through it.
    /// </summary>
    /// <returns>The headroom of one job in bytes; 0 = not stated.</returns>
    public Task<long> GetJobStorageHeadroomBytesAsync()
    {
        return Task.FromResult(JobStorageHeadroomBytes);
    }

    #endregion

    #region Properties

    /// <summary>The headroom the rig states (0 = none, the default).</summary>
    public long JobStorageHeadroomBytes { get; set; }

    /// <summary>The retention window the rig states, as the server's Nodes:JobBlobRetentionDays (0 = not stated).</summary>
    public int JobBlobRetentionDays { get; set; }

    #endregion
}
