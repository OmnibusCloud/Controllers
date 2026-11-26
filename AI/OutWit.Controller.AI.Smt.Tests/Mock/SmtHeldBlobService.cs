using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// A node's blob channel whose calls end when the test says so, and not before: a call that ends
/// late, or never. The channel's calls take no token, so a transfer that gives a call up does not
/// stop it; this is the channel a test needs to say what happens beside such a call. Every call
/// is counted when it starts, and ended by its place among the calls of its kind.
/// </summary>
internal sealed class SmtHeldBlobService : IWitBlobService
{
    #region Fields

    private readonly object m_gate = new();

    private readonly List<TaskCompletionSource<string>> m_downloads = [];

    private readonly List<TaskCompletionSource<Guid>> m_uploads = [];

    private readonly List<(int Count, TaskCompletionSource Started)> m_waiting = [];

    private int m_downloadsEndedFrom = int.MaxValue;

    private string m_downloadsEndWith = "";

    #endregion

    #region Functions

    /// <summary>A task that ends when so many downloads have been started: a test waits for the calls, not for a time.</summary>
    /// <param name="count">The downloads.</param>
    /// <returns>The task.</returns>
    public Task Started(int count)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (m_gate)
        {
            if (m_downloads.Count >= count)
                started.TrySetResult();
            else
                m_waiting.Add((count, started));
        }

        return started.Task;
    }

    /// <summary>Ends a download with the path it gives.</summary>
    /// <param name="call">The download's place among the downloads, from 0.</param>
    /// <param name="path">The local path.</param>
    public void EndDownload(int call, string path)
    {
        Download(call).TrySetResult(path);
    }

    /// <summary>Ends a download with a failure of the channel's.</summary>
    /// <param name="call">The download's place among the downloads, from 0.</param>
    public void FailDownload(int call)
    {
        Download(call).TrySetException(new IOException("The test's blob channel lost the download."));
    }

    /// <summary>Every download from a place on ends at once, with a path: the ones before it stay held.</summary>
    /// <param name="call">The first download that ends at once, from 0.</param>
    /// <param name="path">The local path they give.</param>
    public void EndDownloadsFrom(int call, string path)
    {
        lock (m_gate)
        {
            m_downloadsEndedFrom = call;
            m_downloadsEndWith = path;
        }
    }

    /// <summary>Ends every download that is still held, each with a failure: a test leaves no call behind.</summary>
    public void FailEveryDownload()
    {
        List<TaskCompletionSource<string>> held;
        lock (m_gate)
            held = [.. m_downloads];

        foreach (var call in held)
            call.TrySetException(new IOException("The test's blob channel lost the download."));
    }

    /// <summary>Ends an upload with the blob it made.</summary>
    /// <param name="call">The upload's place among the uploads, from 0.</param>
    /// <param name="blobId">The blob.</param>
    public void EndUpload(int call, Guid blobId)
    {
        lock (m_gate)
            m_uploads[call].TrySetResult(blobId);
    }

    private TaskCompletionSource<string> Download(int call)
    {
        lock (m_gate)
            return m_downloads[call];
    }

    #endregion

    #region IWitBlobService

    /// <inheritdoc />
    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        var call = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (m_gate)
        {
            if (m_downloads.Count >= m_downloadsEndedFrom)
                call.TrySetResult(m_downloadsEndWith);

            m_downloads.Add(call);
            foreach (var (count, started) in m_waiting)
            {
                if (m_downloads.Count >= count)
                    started.TrySetResult();
            }
        }

        return call.Task;
    }

    /// <inheritdoc />
    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        var call = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (m_gate)
            m_uploads.Add(call);

        return call.Task;
    }

    /// <inheritdoc />
    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        return UploadFileAsync(fileName);
    }

    #endregion

    #region Properties

    /// <summary>The downloads started so far.</summary>
    public int Downloads
    {
        get
        {
            lock (m_gate)
                return m_downloads.Count;
        }
    }

    /// <summary>The uploads started so far.</summary>
    public int Uploads
    {
        get
        {
            lock (m_gate)
                return m_uploads.Count;
        }
    }

    #endregion
}
