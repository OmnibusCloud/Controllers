using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// A loopback origin that speaks raw bytes, for what an HTTP library will not send on purpose: a
/// body without a length that ends when the connection closes, a body that trickles, a
/// <c>Retry-After</c> header. Every connection serves one request: the script gets the number of
/// the request (from 1) and the stream, and the connection closes when it returns.
/// </summary>
internal sealed class CorpusRawOrigin : IDisposable
{
    #region Fields

    private readonly TcpListener m_listener;
    private readonly CancellationTokenSource m_stop = new();
    private readonly Func<int, string, NetworkStream, CancellationToken, Task> m_script;
    private readonly Task m_loop;
    private int m_requests;

    #endregion

    #region Constructors

    /// <summary>Starts the origin.</summary>
    /// <param name="script">Answers one request: its number from 1, its head, the stream to write to.</param>
    public CorpusRawOrigin(Func<int, string, NetworkStream, CancellationToken, Task> script)
    {
        m_script = script;
        m_listener = new TcpListener(IPAddress.Loopback, 0);
        m_listener.Start();
        Port = ((IPEndPoint)m_listener.LocalEndpoint).Port;
        m_loop = Task.Run(Loop);
    }

    #endregion

    #region Functions

    /// <summary>The URL of a shard on this origin.</summary>
    public string Url(string name)
    {
        return $"http://127.0.0.1:{Port}/shards/{name}";
    }

    /// <summary>The head of a response with the given status line and headers.</summary>
    public static byte[] Head(string statusLine, params string[] headers)
    {
        return Encoding.ASCII.GetBytes($"HTTP/1.1 {statusLine}\r\n" + string.Concat(headers.Select(header => header + "\r\n")) + "\r\n");
    }

    private async Task Loop()
    {
        while (!m_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await m_listener.AcceptTcpClientAsync(m_stop.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buffer = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(buffer, m_stop.Token) == 0)
                        return;

                    head.Append((char)buffer[0]);
                }

                await m_script(Interlocked.Increment(ref m_requests), head.ToString(), stream, m_stop.Token);
                await stream.FlushAsync(m_stop.Token);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // the client went away, or the origin is stopping
            }
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Stops the origin.</summary>
    public void Dispose()
    {
        m_stop.Cancel();
        m_listener.Stop();
    }

    #endregion

    #region Properties

    /// <summary>The port the origin listens on.</summary>
    public int Port { get; }

    /// <summary>The host name tasks allow.</summary>
    public string Host => "127.0.0.1";

    /// <summary>Requests answered so far.</summary>
    public int Requests => m_requests;

    #endregion
}
