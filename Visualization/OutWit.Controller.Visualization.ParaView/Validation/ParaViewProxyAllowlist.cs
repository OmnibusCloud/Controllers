using System.Text.Json;
using OutWit.Controller.Visualization.ParaView.Runtime;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// The proxy allowlist tied to the pinned ParaView runtime: the set of group/type keys a state may
/// instantiate, plus the proxies each allowlisted plugin contributes. Loaded from the versioned
/// artifact embedded in the controller assembly (<c>allowlists/paraview-&lt;major.minor&gt;.json</c>),
/// which the runtime-proof tooling generates from the fixture corpus and commits as a reviewed change.
/// </summary>
public sealed class ParaViewProxyAllowlist
{
    #region Constants

    private const string RESOURCE_PREFIX = "allowlists/paraview-";

    private const string RESOURCE_SUFFIX = ".json";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Lazy<ParaViewProxyAllowlist> BUNDLED = new(() => LoadEmbedded(ParaViewRuntimeInfo.RUNTIME_SERIES));

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an allowlist from explicit keys (tests, generation tooling).
    /// </summary>
    /// <param name="runtimeVersion">The major.minor runtime version the list is tied to.</param>
    /// <param name="origin">Provenance marker (seed, generated).</param>
    /// <param name="proxies">Allowlisted group/type keys.</param>
    /// <param name="pluginProxies">Plugin name → the group/type keys it contributes.</param>
    public ParaViewProxyAllowlist(
        string runtimeVersion,
        string origin,
        IEnumerable<string> proxies,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? pluginProxies = null)
    {
        RuntimeVersion = runtimeVersion;
        Origin = origin;
        Proxies = new HashSet<string>(proxies, StringComparer.Ordinal);
        PluginProxies = pluginProxies ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Loads the embedded allowlist for a runtime major.minor version.
    /// </summary>
    /// <param name="runtimeVersion">The runtime major.minor, for example 6.1.</param>
    /// <returns>The allowlist.</returns>
    /// <exception cref="InvalidOperationException">No allowlist is embedded for that version.</exception>
    public static ParaViewProxyAllowlist LoadEmbedded(string runtimeVersion)
    {
        var resourceName = RESOURCE_PREFIX + runtimeVersion + RESOURCE_SUFFIX;
        using var stream = typeof(ParaViewProxyAllowlist).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The ParaView controller carries no proxy allowlist for runtime {runtimeVersion} ('{resourceName}').");

        var allowlist = Load(stream);
        if (!string.Equals(allowlist.RuntimeVersion, runtimeVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"The embedded proxy allowlist '{resourceName}' declares ParaView {allowlist.RuntimeVersion}, not {runtimeVersion}.");

        return allowlist;
    }

    /// <summary>
    /// Loads an allowlist document.
    /// </summary>
    /// <param name="stream">The JSON document.</param>
    /// <returns>The allowlist.</returns>
    /// <exception cref="InvalidOperationException">The document is malformed.</exception>
    public static ParaViewProxyAllowlist Load(Stream stream)
    {
        var document = JsonSerializer.Deserialize<ParaViewProxyAllowlistDocument>(stream, JSON_OPTIONS)
            ?? throw new InvalidOperationException("The proxy allowlist document is empty.");

        if (document.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported proxy allowlist schema version {document.SchemaVersion}.");

        if (string.IsNullOrWhiteSpace(document.ParaView))
            throw new InvalidOperationException("The proxy allowlist document names no ParaView version.");

        var plugins = (document.PluginProxies ?? new Dictionary<string, List<string>>())
            .ToDictionary(me => me.Key, me => (IReadOnlyList<string>)me.Value.ToList(), StringComparer.Ordinal);

        return new ParaViewProxyAllowlist(document.ParaView, document.Origin ?? "unknown", document.Proxies ?? [], plugins);
    }

    /// <summary>
    /// Whether a group/type key is admissible given the plugins the package requires.
    /// </summary>
    /// <param name="key">group/type.</param>
    /// <param name="requiredPlugins">Names of the (already validated) required plugins.</param>
    /// <returns>True when allowlisted.</returns>
    public bool Allows(string key, IEnumerable<string> requiredPlugins)
    {
        if (Proxies.Contains(key))
            return true;

        foreach (var plugin in requiredPlugins)
        {
            if (PluginProxies.TryGetValue(plugin, out var contributed) && contributed.Contains(key, StringComparer.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The effective allowlisted keys for a set of required plugins (what the runner re-checks after load).
    /// </summary>
    /// <param name="requiredPlugins">Names of the required plugins.</param>
    /// <returns>Sorted keys.</returns>
    public IReadOnlyList<string> EffectiveKeys(IEnumerable<string> requiredPlugins)
    {
        var keys = new SortedSet<string>(Proxies, StringComparer.Ordinal);
        foreach (var plugin in requiredPlugins)
        {
            if (PluginProxies.TryGetValue(plugin, out var contributed))
                keys.UnionWith(contributed);
        }

        return keys.ToList();
    }

    #endregion

    #region Properties

    /// <summary>The allowlist embedded for the pinned runtime, loaded once per process.</summary>
    public static ParaViewProxyAllowlist Bundled => BUNDLED.Value;

    /// <summary>The runtime major.minor the list is tied to.</summary>
    public string RuntimeVersion { get; }

    /// <summary>Provenance of the list (seed or generated).</summary>
    public string Origin { get; }

    /// <summary>Allowlisted group/type keys of the core runtime.</summary>
    public IReadOnlySet<string> Proxies { get; }

    /// <summary>Plugin name → the group/type keys the plugin contributes.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> PluginProxies { get; }

    #endregion
}
