using OutWit.Cloud.SDK.Blobs;
using OutWit.Controller.AI.Smt.Live.Server;

namespace OutWit.Controller.AI.Smt.Live.Tests.Mock;

/// <summary>
/// A server in memory: blobs are byte arrays by id, a submitted script is recorded and answered
/// by the job the test makes for it, a job is opened by its id from the jobs the test puts
/// there, and a blob the test names fails its download.
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
        var job = Jobs(script, parameters);
        job.Script = script;
        return Task.FromResult<ISmtLiveJob>(job);
    }

    public Task<ISmtLiveJob> OpenAsync(Guid id, CancellationToken ct)
    {
        return Existing.TryGetValue(id, out var job)
            ? Task.FromResult<ISmtLiveJob>(job)
            : Task.FromException<ISmtLiveJob>(new InvalidOperationException($"Job {id} not found"));
    }

    public async Task DownloadAsync(Guid blob, string path, IProgress<WitTransferProgress>? progress, CancellationToken ct)
    {
        if (Failing.Contains(blob))
            throw new IOException("the connection was reset");

        await File.WriteAllBytesAsync(path, m_blobs[blob], ct);
        progress?.Report(new WitTransferProgress(m_blobs[blob].Length, m_blobs[blob].Length));
    }

    #endregion

    #region Properties

    /// <summary>Makes the job of a submitted script.</summary>
    public Func<string, IReadOnlyList<object?>, SmtLiveJobFake> Jobs { get; set; } = (script, _) => throw new InvalidOperationException($"no job for {script}");

    /// <summary>The jobs a job id opens.</summary>
    public Dictionary<Guid, SmtLiveJobFake> Existing { get; } = [];

    /// <summary>The blobs whose download fails.</summary>
    public HashSet<Guid> Failing { get; } = [];

    /// <summary>The scripts submitted, with their parameters.</summary>
    public List<(string Script, IReadOnlyList<object?> Parameters)> Submitted { get; } = [];

    /// <summary>The blobs uploaded.</summary>
    public List<Guid> Uploaded { get; } = [];

    /// <summary>The blobs on the server, by id.</summary>
    public IReadOnlyDictionary<Guid, byte[]> Blobs => m_blobs;

    #endregion
}
