using System.Net;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A public origin on the loopback: serves the golden shards under /shards/&lt;name&gt; and
/// scripted misbehaviour under /fail/... (a run of 503s before success, a stall, a body that
/// lies about its length) so the fetcher's retries and caps are tested against a real socket.
/// </summary>
public sealed class CorpusTestOrigin : IDisposable
{
    #region Fields

    private readonly HttpListener m_listener = new();
    private readonly string m_shardsDirectory;
    private readonly CancellationTokenSource m_stop = new();
    private readonly Task m_loop;
    private int m_failuresLeft;
    private int m_cutAfter = -1;
    private int m_inFlight;
    private int m_maxInFlight;

    #endregion

    #region Constructors

    public CorpusTestOrigin(string shardsDirectory)
    {
        m_shardsDirectory = shardsDirectory;
        Port = FreePort();
        m_listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        m_listener.Start();
        m_loop = Task.Run(Loop);
    }

    #endregion

    #region Functions

    /// <summary>The URL of a golden shard.</summary>
    public string ShardUrl(string name)
    {
        return $"{BaseUrl}shards/{name}";
    }

    /// <summary>A URL that answers 503 the given number of times, then serves the shard.</summary>
    public string FlakyUrl(string name, int failures)
    {
        m_failuresLeft = failures;
        return $"{BaseUrl}flaky/{name}";
    }

    /// <summary>A URL that serves the shard after a pause, to watch how many requests a client keeps in flight.</summary>
    public string SlowUrl(string name)
    {
        return $"{BaseUrl}slow/{name}";
    }

    /// <summary>A URL whose first response breaks after the given number of body bytes; later requests honour Range.</summary>
    public string CutUrl(string name, int bytesBeforeCut)
    {
        m_cutAfter = bytesBeforeCut;
        return $"{BaseUrl}cut/{name}";
    }

    private async Task Loop()
    {
        while (!m_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await m_listener.GetContextAsync();
            }
            catch (Exception) when (m_stop.IsCancellationRequested || !m_listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => Serve(context));
        }
    }

    private void Serve(HttpListenerContext context)
    {
        Requests++;
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var response = context.Response;
        var inFlight = Interlocked.Increment(ref m_inFlight);
        var seen = m_maxInFlight;
        while (inFlight > seen && Interlocked.CompareExchange(ref m_maxInFlight, inFlight, seen) != seen)
            seen = m_maxInFlight;

        try
        {
            if (path.StartsWith("/slow/", StringComparison.Ordinal))
            {
                Thread.Sleep(400);
                path = "/shards/" + Path.GetFileName(path);
            }

            if (context.Request.HttpMethod == "HEAD" && path.StartsWith("/shards/", StringComparison.Ordinal))
            {
                var headFile = Path.Combine(m_shardsDirectory, Path.GetFileName(path));
                response.StatusCode = File.Exists(headFile) ? 200 : 404;
                if (File.Exists(headFile))
                    response.ContentLength64 = new FileInfo(headFile).Length;
                response.Close();
                return;
            }

            if (path.StartsWith("/flaky/", StringComparison.Ordinal) && Interlocked.Decrement(ref m_failuresLeft) >= 0)
            {
                response.StatusCode = 503;
                response.Close();
                return;
            }

            if (path.StartsWith("/shards/", StringComparison.Ordinal) || path.StartsWith("/flaky/", StringComparison.Ordinal) || path.StartsWith("/cut/", StringComparison.Ordinal))
            {
                var file = Path.Combine(m_shardsDirectory, Path.GetFileName(path));
                if (!File.Exists(file))
                {
                    response.StatusCode = 404;
                    response.Close();
                    return;
                }

                var bytes = File.ReadAllBytes(file);
                var from = 0;
                var range = context.Request.Headers["Range"];
                if (range != null && range.StartsWith("bytes=", StringComparison.Ordinal) && int.TryParse(range[6..].TrimEnd('-'), out from) && from > 0 && from < bytes.Length)
                {
                    RangeRequests++;
                    response.StatusCode = 206;
                    response.Headers["Content-Range"] = $"bytes {from}-{bytes.Length - 1}/{bytes.Length}";
                }
                else
                    from = 0;

                response.ContentType = "application/octet-stream";
                response.ContentLength64 = bytes.Length - from;

                var cut = path.StartsWith("/cut/", StringComparison.Ordinal) ? Interlocked.Exchange(ref m_cutAfter, -1) : -1;
                if (cut >= 0)
                {
                    // The first answer breaks mid-body: a few bytes, then the connection dies.
                    response.OutputStream.Write(bytes, from, Math.Min(cut, bytes.Length - from));
                    response.OutputStream.Flush();
                    response.Abort();
                    return;
                }

                response.OutputStream.Write(bytes, from, bytes.Length - from);
                response.Close();
                return;
            }

            if (path == "/forbidden")
            {
                response.StatusCode = 403;
                response.Close();
                return;
            }

            if (path == "/unsized")
            {
                // No Content-Length: the cap must catch the body while it streams.
                response.SendChunked = true;
                var chunk = new byte[64 * 1024];
                for (var i = 0; i < 64; i++)
                    response.OutputStream.Write(chunk);
                response.Close();
                return;
            }

            if (path == "/garbage")
            {
                var bytes = Encoding.ASCII.GetBytes("this is not a gzip file, nor a WARC file, nor anything a node should accept");
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes);
                response.Close();
                return;
            }

            response.StatusCode = 404;
            response.Close();
        }
        catch (Exception)
        {
            try { response.Abort(); } catch { /* the client went away */ }
        }
        finally
        {
            Interlocked.Decrement(ref m_inFlight);
        }
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        m_stop.Cancel();
        try { m_listener.Stop(); m_listener.Close(); } catch { /* already closed */ }
        try { m_loop.Wait(TimeSpan.FromSeconds(5)); } catch { /* the loop ended with the listener */ }
    }

    #endregion

    #region Properties

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    public string Host => "127.0.0.1";

    public int Requests { get; private set; }

    public int RangeRequests { get; private set; }

    /// <summary>Most requests this origin has seen in flight at once.</summary>
    public int MaxInFlight => m_maxInFlight;

    #endregion
}
