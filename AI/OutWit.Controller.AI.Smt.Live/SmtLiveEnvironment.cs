namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// Where the tool connects and with what: the server, the identity server and the application
/// key, each from an environment variable. Nothing has a default: no address is written into the
/// tool. The key is handed to the SDK and to nothing else; it is never printed.
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
    /// Reads the three variables.
    /// </summary>
    /// <param name="variable">Gives the value of a variable by its name, or null.</param>
    /// <param name="error">Which variables are not set; null when all are.</param>
    /// <returns>The environment; null when a variable is not set.</returns>
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

        error = null;
        return new SmtLiveEnvironment(server, identity, key);
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
