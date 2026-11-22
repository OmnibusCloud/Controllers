using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The HTTP plumbing every request of the controller goes through, on a node and on the host
/// alike: a client that connects only to addresses the machine's policy accepts and never
/// follows a redirect on its own, and a send that follows redirects one hop at a time,
/// asking the allowlist gate about every hop. Each hop may have a time for its answer's head:
/// an origin that takes the request and never answers it is then the same broken answer a
/// body that stalls is.
/// </summary>
public static class ShardHttp
{
    #region Constants

    /// <summary>Redirects followed at most for one request.</summary>
    public const int MAX_REDIRECTS = 5;

    /// <summary>What the origin sees: the controller's name and version.</summary>
    public static readonly string USER_AGENT = $"OutWit-Corpus/{ControllerBuildInfo.VERSION}";

    #endregion

    #region Functions

    /// <summary>
    /// Creates the client: no client-side timeout (the caller bounds its waits), automatic
    /// decompression off (shards are gzip and stay gzip), automatic redirects off, and every
    /// connection made to an address the policy resolved and accepted.
    /// </summary>
    /// <param name="policy">What this machine fetches from.</param>
    /// <returns>The client.</returns>
    public static HttpClient CreateClient(ShardOriginPolicy policy)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(30),
            ConnectCallback = (context, cancellationToken) => ConnectAsync(policy, context, cancellationToken)
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(ProductInfoHeaderValue.Parse(USER_AGENT));
        return client;
    }

    /// <summary>Sends a request and follows its redirects, each one checked against the task's list and the machine's policy before it is followed.</summary>
    /// <param name="client">A client from <see cref="CreateClient"/>.</param>
    /// <param name="method">GET or HEAD.</param>
    /// <param name="uri">The URL, already checked.</param>
    /// <param name="rangeFrom">The first byte wanted (0 = the whole body).</param>
    /// <param name="allowedOrigins">Host names the task allows.</param>
    /// <param name="policy">What this machine fetches from.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <param name="headLimit">The longest each hop may take to send its answer's head (the request's connection included); null for no limit of its own (the caller's token bounds the wait).</param>
    /// <returns>The response of the last hop, headers read; the caller disposes it.</returns>
    /// <exception cref="ShardOriginRefusedException">A redirect points somewhere the task or the machine does not fetch from, or there are too many of them.</exception>
    /// <exception cref="IOException">A hop sent no answer head within <paramref name="headLimit"/>.</exception>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, Uri uri, long rangeFrom, IReadOnlyCollection<string> allowedOrigins, ShardOriginPolicy policy, CancellationToken cancellationToken, TimeSpan? headLimit = null)
    {
        var current = uri;

        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(method, current);
            if (rangeFrom > 0)
                request.Headers.Range = new RangeHeaderValue(rangeFrom, null);

            var response = await HeadAsync(client, request, cancellationToken, headLimit);
            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null)
                return response;

            var location = response.Headers.Location;
            response.Dispose();

            if (hop >= MAX_REDIRECTS)
                throw new ShardOriginRefusedException(Trust.CorpusResultCodes.ORIGIN_REDIRECT, $"'{uri}' redirects more than {MAX_REDIRECTS} times.");

            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            var refusal = ShardOrigin.Check(next.AbsoluteUri, allowedOrigins, policy, out var allowed);
            if (refusal != null || allowed == null)
                throw new ShardOriginRefusedException(Trust.CorpusResultCodes.ORIGIN_REDIRECT, $"'{current}' redirects to a place that is not fetched: {refusal}");

            current = allowed;
        }
    }

    #endregion

    #region Tools

    /// <summary>Sends one hop's request and waits for its answer's head, within the hop's limit.</summary>
    private static async Task<HttpResponseMessage> HeadAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken, TimeSpan? headLimit)
    {
        if (headLimit is not { } limit || limit == Timeout.InfiniteTimeSpan)
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // The hop's own token ends with the hop: the body that follows is bounded by the caller.
        using var head = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        head.CancelAfter(limit);
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, head.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("The origin sent no answer head in time.");
        }
    }

    private static bool IsRedirect(HttpStatusCode status)
    {
        return status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    }

    private static async ValueTask<Stream> ConnectAsync(ShardOriginPolicy policy, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;

        // Behind a proxy the connection goes to the proxy, which resolves the origin itself;
        // the address rule cannot apply there. The host rules still do, and the public
        // origins are HTTPS, so the certificate says who answered.
        var target = context.InitialRequestMessage.RequestUri?.IdnHost;
        var direct = string.Equals(target, endpoint.Host, StringComparison.OrdinalIgnoreCase);

        var addresses = direct
            ? await policy.ResolveAsync(endpoint.Host, cancellationToken)
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                last = exception;
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    #endregion
}
