using OutWit.Cloud.SDK;
using OutWit.Cloud.SDK.Blobs;

namespace OutWit.Controller.AI.Smt.Live.Server;

/// <summary>
/// A real server, through the public SDK: the client signs in with the application key, blobs
/// go up and down in chunks (resumed after a break by the SDK), and scripts are submitted with
/// positional parameters, as a lab's application does it.
/// </summary>
internal sealed class SmtLiveServerCloud : ISmtLiveServer, IAsyncDisposable
{
    #region Constants

    /// <summary>How often the status of a job is asked for: the error of a job's time as the tool measures it.</summary>
    public static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a job's status may not be read before the tool stops waiting for it (the job goes
    /// on on the server): a connection that drops for some minutes during an hour's run is waited
    /// out.
    /// </summary>
    public static readonly TimeSpan POLL_TOLERANCE = TimeSpan.FromMinutes(10);

    #endregion

    #region Fields

    private readonly WitCloudClient m_client;
    private readonly TextWriter m_output;

    #endregion

    #region Constructors

    private SmtLiveServerCloud(WitCloudClient client, TextWriter output)
    {
        m_client = client;
        m_output = output;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Connects to the server the environment names and prints where, never with the key.
    /// </summary>
    /// <param name="environment">The server, the identity server and the key.</param>
    /// <param name="output">Where the lines go.</param>
    /// <param name="ct">Cancels the connect.</param>
    /// <returns>The connected server.</returns>
    public static async Task<SmtLiveServerCloud> ConnectAsync(SmtLiveEnvironment environment, TextWriter output, CancellationToken ct)
    {
        output.WriteLine($"server   : {environment.Server}");
        output.WriteLine($"identity : {environment.Identity}");

        var client = new WitCloudClient(environment.Server, environment.Identity, environment.Key);
        try
        {
            await client.ConnectAsync(ct);
            var scope = await client.GetExecutionScopeOptionsAsync(ct);
            output.WriteLine($"scope    : {scope.Groups.Length} group(s), {scope.Projects.Length} project(s)");
            return new SmtLiveServerCloud(client, output);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    #endregion

    #region ISmtLiveServer

    /// <inheritdoc/>
    public Task<Guid> UploadAsync(string path, IProgress<WitTransferProgress>? progress, CancellationToken ct)
    {
        return m_client.Blobs.UploadBlobFromFileAsync(path, progress: progress, ct: ct);
    }

    /// <inheritdoc/>
    public async Task<ISmtLiveJob> SubmitAsync(string script, IReadOnlyList<object?> parameters, CancellationToken ct)
    {
        var handle = await m_client.Scripts.SubmitAsync(script, parameters, ct: ct);
        return new SmtLiveJobCloud(m_client.Jobs, handle.JobId, script, POLL_INTERVAL, POLL_TOLERANCE, m_output);
    }

    /// <inheritdoc/>
    public async Task<ISmtLiveJob> OpenAsync(Guid id, CancellationToken ct)
    {
        var job = await m_client.Jobs.GetStatusAsync(id, ct);
        return new SmtLiveJobCloud(m_client.Jobs, id, job.ScriptName ?? "", POLL_INTERVAL, POLL_TOLERANCE, m_output);
    }

    /// <inheritdoc/>
    public Task DownloadAsync(Guid blob, string path, IProgress<WitTransferProgress>? progress, CancellationToken ct)
    {
        return m_client.Blobs.DownloadBlobToFileAsync(blob, path, progress: progress, ct: ct);
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return m_client.DisposeAsync();
    }

    #endregion
}
