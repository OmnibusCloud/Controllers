using OutWit.Cloud.SDK.Blobs;
using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// A server in memory: blobs are byte arrays by id, a submitted script is recorded and answered
/// by the job the test makes for it.
/// </summary>
internal sealed class SmtLiveServerFake : ISmtLiveServer
{
    #region Fields

    private readonly Dictionary<Guid, byte[]> m_blobs = [];

    #endregion

    #region Functions

    /// <summary>Puts a blob on the server.</summary>
    /// <param name="bytes">Its bytes.</param>
    /// <returns>Its id.</returns>
    public Guid Add(byte[] bytes)
    {
        var id = Guid.NewGuid();
        m_blobs[id] = bytes;
        return id;
    }

    /// <summary>Puts a file on the server as a blob.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its id.</returns>
    public Guid AddFile(string path)
    {
        return Add(File.ReadAllBytes(path));
    }

    #endregion

    #region ISmtLiveServer

    public Task<Guid> UploadAsync(string path, IProgress<WitTransferProgress>? progress, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(path);
        progress?.Report(new WitTransferProgress(bytes.Length, bytes.Length));
        var id = Add(bytes);
        Uploaded.Add(id);
        return Task.FromResult(id);
    }

    public Task<ISmtLiveJob> SubmitAsync(string script, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        Submitted.Add((script, parameters));
        return Task.FromResult<ISmtLiveJob>(Jobs(script, parameters));
    }

    public async Task DownloadAsync(Guid blob, string path, IProgress<WitTransferProgress>? progress, CancellationToken ct)
    {
        await File.WriteAllBytesAsync(path, m_blobs[blob], ct);
        progress?.Report(new WitTransferProgress(m_blobs[blob].Length, m_blobs[blob].Length));
    }

    #endregion

    #region Properties

    /// <summary>Makes the job of a submitted script.</summary>
    public Func<string, IReadOnlyList<object?>, SmtLiveJobFake> Jobs { get; set; } = (script, _) => throw new InvalidOperationException($"no job for {script}");

    /// <summary>The scripts submitted, with their parameters.</summary>
    public List<(string Script, IReadOnlyList<object?> Parameters)> Submitted { get; } = [];

    /// <summary>The blobs uploaded.</summary>
    public List<Guid> Uploaded { get; } = [];

    /// <summary>The blobs on the server, by id.</summary>
    public IReadOnlyDictionary<Guid, byte[]> Blobs => m_blobs;

    #endregion
}
