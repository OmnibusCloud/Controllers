using OutWit.Cloud.SDK.Blobs;

namespace OutWit.Controller.AI.Smt.Live.Server;

/// <summary>
/// What the tool asks of a server: a file uploaded as a blob, a script submitted with its
/// parameters, a blob downloaded to a file. Big data goes by blobs both ways, in chunks: a
/// problem set and the documents never travel as a parameter or a result.
/// </summary>
internal interface ISmtLiveServer
{
    /// <summary>
    /// Uploads a file as a blob.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="progress">Told the bytes sent so far.</param>
    /// <param name="ct">Cancels the local call.</param>
    /// <returns>The blob's id.</returns>
    Task<Guid> UploadAsync(string path, IProgress<WitTransferProgress>? progress, CancellationToken ct);

    /// <summary>
    /// Submits a bundled script.
    /// </summary>
    /// <param name="script">The script's name.</param>
    /// <param name="parameters">Its parameters in order.</param>
    /// <param name="ct">Cancels the local call.</param>
    /// <returns>The job.</returns>
    Task<ISmtLiveJob> SubmitAsync(string script, IReadOnlyList<object?> parameters, CancellationToken ct);

    /// <summary>
    /// Downloads a blob to a file.
    /// </summary>
    /// <param name="blob">The blob's id.</param>
    /// <param name="path">The file; replaced when it is there.</param>
    /// <param name="progress">Told the bytes received so far.</param>
    /// <param name="ct">Cancels the local call.</param>
    /// <returns>A task.</returns>
    Task DownloadAsync(Guid blob, string path, IProgress<WitTransferProgress>? progress, CancellationToken ct);
}
