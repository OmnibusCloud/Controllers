using System.Net;

namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// Where the tool connects and with what: the server, the identity server and the application
/// key, each from an environment variable. Nothing has a default: no address is written into the
/// tool. The key is handed to the SDK and to nothing else; it is never printed. Both addresses
/// must be <c>https://</c> (the key is exchanged with the identity server, and the token it gives
/// goes to the server), or <c>http://</c> to this machine's own loopback for a local test: the
/// SDK would send a long-lived key in clear to any other <c>http://</c> address.
/// </summary>
internal sealed class SmtLiveEnvironment
{
    #region Constants

    /// <summary>The URL of the server.</summary>
    public const string SERVER_VARIABLE = "WITAI_SMT_LIVE_SERVER";

    /// <summary>The URL of the identity server.</summary>
    public const string IDENTITY_VARIABLE = "WITAI_SMT_LIVE_IDENTITY";

    /// <summary>The application key (<c>wit_sk_...</c>): the variable the Verify and Corpus live tools read it from too.</summary>
    public const string KEY_VARIABLE = "WITAI_VERIFY_LIVE_KEY";

    /// <summary>Where the key may go, as the refusal and the usage say it.</summary>
    public const string WHERE_THE_KEY_GOES = "the key goes only to an https:// address, or to this machine (http://localhost) for a local test";

    #endregion

    #region Constructors

    private SmtLiveEnvironment(string server, string identity, string key)
    {
        Server = server;
        Identity = identity;
        Key = key;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the three variables and holds the two addresses to where the key may go.
    /// </summary>
    /// <param name="variable">Gives the value of a variable by its name, or null.</param>
    /// <param name="error">Which variables are not set, or which address is refused; null when all are taken.</param>
    /// <returns>The environment; null when a variable is not set or an address is refused.</returns>
    public static SmtLiveEnvironment? Read(Func<string, string?> variable, out string? error)
    {
        var server = variable(SERVER_VARIABLE);
        var identity = variable(IDENTITY_VARIABLE);
        var key = variable(KEY_VARIABLE);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(server))
            missing.Add($"{SERVER_VARIABLE} (the server's URL)");

        if (string.IsNullOrWhiteSpace(identity))
            missing.Add($"{IDENTITY_VARIABLE} (the identity server's URL)");

        if (string.IsNullOrWhiteSpace(key))
            missing.Add($"{KEY_VARIABLE} (a wit_sk_ application key)");

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(key))
        {
            error = $"not set: {string.Join(", ", missing)}";
            return null;
        }

        // The values are not printed: a key put into the wrong variable stays unsaid.
        var refused = new[] { (SERVER_VARIABLE, server), (IDENTITY_VARIABLE, identity) }
            .Where(address => !MayTakeTheKey(address.Item2))
            .Select(address => address.Item1)
            .ToList();
        if (refused.Count > 0)
        {
            error = $"refused: {string.Join(" and ", refused)} {(refused.Count == 1 ? "is not an address" : "are not addresses")} the key may go to: {WHERE_THE_KEY_GOES}";
            return null;
        }

        error = null;
        return new SmtLiveEnvironment(server, identity, key);
    }

    /// <summary>Whether an address may be sent the key: an absolute https:// address, or http:// to this machine's loopback.</summary>
    private static bool MayTakeTheKey(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || IPAddress.TryParse(uri.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip));
    }

    #endregion

    #region Properties

    /// <summary>The URL of the server.</summary>
    public string Server { get; }

    /// <summary>The URL of the identity server.</summary>
    public string Identity { get; }

    /// <summary>The application key; never printed.</summary>
    public string Key { get; }

    #endregion
}
