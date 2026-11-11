using System.Net;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// What a machine is willing to fetch from, whatever a task says. A task's own list of hosts
/// is written by whoever submitted the job, so it can only narrow this policy, never widen
/// it: without the policy a job could send a contributor's machine to any address, the ones
/// inside that contributor's network included.
/// <para>
/// Two kinds of host pass. The public origins compiled into the controller, over HTTPS on the
/// default port and at public addresses only. And the hosts the machine's owner has listed in
/// <see cref="EXTRA_ORIGINS_VARIABLE"/>: an explicit choice by the party that carries the
/// risk, so those are taken as written (HTTP, any port, any address) — a lab's own data
/// server inside its network is the case.
/// </para>
/// </summary>
public sealed class ShardOriginPolicy
{
    #region Constants

    /// <summary>The environment variable in which a machine's owner lists further hosts, separated by semicolons, commas or spaces.</summary>
    public const string EXTRA_ORIGINS_VARIABLE = "OMNIBUSCLOUD_CORPUS_ORIGINS";

    /// <summary>The public origins every machine fetches from: Common Crawl's download host.</summary>
    public static readonly IReadOnlyList<string> PUBLIC_ORIGINS = ["data.commoncrawl.org"];

    private static readonly Lazy<ShardOriginPolicy> MACHINE = new(() => Parse(Environment.GetEnvironmentVariable(EXTRA_ORIGINS_VARIABLE)));

    private static readonly char[] SEPARATORS = [';', ',', ' ', '\t', '\r', '\n'];

    #endregion

    #region Fields

    private readonly HashSet<string> m_extra;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> m_resolver;

    #endregion

    #region Constructors

    /// <summary>Creates a policy.</summary>
    /// <param name="extraOrigins">Hosts the machine's owner allows beyond the public origins (null = none).</param>
    /// <param name="resolver">Resolves a host name to addresses (null = the system resolver).</param>
    public ShardOriginPolicy(IEnumerable<string>? extraOrigins = null, Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        m_extra = new HashSet<string>(
            (extraOrigins ?? []).Select(ShardOrigin.Normalize).Where(host => host.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        m_resolver = resolver ?? ((host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken));
    }

    #endregion

    #region Functions

    /// <summary>Reads a policy from the text of <see cref="EXTRA_ORIGINS_VARIABLE"/>.</summary>
    /// <param name="variable">The variable's value (null or empty = the public origins only).</param>
    /// <returns>The policy.</returns>
    public static ShardOriginPolicy Parse(string? variable)
    {
        return new ShardOriginPolicy((variable ?? "").Split(SEPARATORS, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>Whether a host is one of the public origins.</summary>
    /// <param name="host">The host name.</param>
    /// <returns>True for a public origin.</returns>
    public static bool IsPublicOrigin(string host)
    {
        return PUBLIC_ORIGINS.Contains(host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the machine's owner listed a host.</summary>
    /// <param name="host">The host name.</param>
    /// <returns>True for a listed host.</returns>
    public bool IsExtraOrigin(string host)
    {
        return m_extra.Contains(host);
    }

    /// <summary>Decides whether this machine may send a request to a URL.</summary>
    /// <param name="uri">The absolute URL.</param>
    /// <returns>Null when the request may be made, else the refusal.</returns>
    public string? Check(Uri uri)
    {
        var host = uri.IdnHost;

        if (IsExtraOrigin(host))
            return null;

        if (!IsPublicOrigin(host))
            return $"'{host}' is not a public origin this controller fetches from ({string.Join(", ", PUBLIC_ORIGINS)}), and this machine's owner has not listed it in {EXTRA_ORIGINS_VARIABLE}.";

        if (uri.Scheme != Uri.UriSchemeHttps)
            return $"'{host}' is fetched over https only.";

        if (!uri.IsDefaultPort)
            return $"'{host}' is fetched on the default port only.";

        return null;
    }

    /// <summary>
    /// Resolves a host this machine is about to connect to and keeps the addresses it may
    /// use: every address of a host the owner listed, the public addresses of anything else.
    /// The caller connects to one of the returned addresses, so the address that was judged
    /// is the address that is used.
    /// </summary>
    /// <param name="host">The host name or address literal.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The usable addresses, at least one.</returns>
    /// <exception cref="ShardOriginRefusedException">The host has no address this machine may connect to.</exception>
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await m_resolver(host, cancellationToken);

        if (IsExtraOrigin(host))
            return addresses;

        var usable = addresses.Where(ShardAddress.IsPublic).ToArray();
        if (usable.Length == 0)
            throw new ShardOriginRefusedException($"'{host}' resolves to no public address ({string.Join(", ", addresses.Select(address => address.ToString()))}).");

        return usable;
    }

    #endregion

    #region Properties

    /// <summary>The policy of the machine this code runs on, read once from the environment.</summary>
    public static ShardOriginPolicy Machine => MACHINE.Value;

    /// <summary>The hosts the machine's owner listed.</summary>
    public IReadOnlyCollection<string> ExtraOrigins => m_extra;

    #endregion
}
