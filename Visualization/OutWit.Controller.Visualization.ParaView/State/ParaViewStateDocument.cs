using System.Globalization;
using System.Xml;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.State;

/// <summary>
/// A ParaView state (.pvsm) parsed as untrusted input: DTD and external entity resolution are
/// prohibited, entity expansion is disabled, and nesting depth, element count, attribute count and
/// text sizes are bounded. Exposes exactly what validation and the runner contract need — the
/// instantiated proxies with their scalar properties, the named proxy collections, the views, and
/// the TimeKeeper timeline — and nothing is evaluated.
/// </summary>
public sealed class ParaViewStateDocument
{
    #region Constants

    private const string VIEWS_COLLECTION = "views";

    private const string TIME_KEEPER_GROUP = "misc";

    private const string TIME_KEEPER_TYPE = "TimeKeeper";

    private const string TIMESTEP_VALUES_PROPERTY = "TimestepValues";

    #endregion

    #region Constructors

    internal ParaViewStateDocument(
        string version,
        IReadOnlyList<ParaViewStateProxy> proxies,
        IReadOnlyDictionary<string, IReadOnlyList<ParaViewStateCollectionItem>> collections,
        bool hasCustomProxyDefinitions)
    {
        Version = version;
        Proxies = proxies;
        Collections = collections;
        HasCustomProxyDefinitions = hasCustomProxyDefinitions;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Parses a state file under the hardened reader settings.
    /// </summary>
    /// <param name="path">Path of the .pvsm file.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="ParaViewStateFormatException">The file is not an admissible state.</exception>
    public static ParaViewStateDocument Parse(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new ParaViewStateFormatException($"state file '{path}' does not exist");

        if (info.Length > ParaViewInputLimits.MAX_STATE_BYTES)
            throw new ParaViewStateFormatException($"state file is {info.Length} bytes, over the {ParaViewInputLimits.MAX_STATE_BYTES} byte limit");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream);
    }

    /// <summary>
    /// Parses a state document from a stream under the hardened reader settings.
    /// </summary>
    /// <param name="stream">UTF-8 XML stream.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="ParaViewStateFormatException">The document is not an admissible state.</exception>
    public static ParaViewStateDocument Parse(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = ParaViewInputLimits.MAX_STATE_BYTES,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false
        };

        try
        {
            using var reader = XmlReader.Create(stream, settings);
            return new ParaViewStateDocumentParser(reader).Parse();
        }
        catch (XmlException e)
        {
            throw new ParaViewStateFormatException($"state is not well-formed XML: {e.Message}", e);
        }
    }

    /// <summary>
    /// Proxies of one group.
    /// </summary>
    /// <param name="group">XML group name.</param>
    /// <returns>The proxies in document order.</returns>
    public IEnumerable<ParaViewStateProxy> ProxiesInGroup(string group)
    {
        return Proxies.Where(me => string.Equals(me.Group, group, StringComparison.Ordinal));
    }

    /// <summary>
    /// Items of a named proxy collection, empty when the collection is absent.
    /// </summary>
    /// <param name="name">Collection name (sources, views, representations, …).</param>
    /// <returns>The items in document order.</returns>
    public IReadOnlyList<ParaViewStateCollectionItem> CollectionItems(string name)
    {
        return Collections.TryGetValue(name, out var items) ? items : [];
    }

    #endregion

    #region Properties

    /// <summary>
    /// The ServerManagerState version attribute (the producing ParaView version), empty when absent.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Every proxy in document order.
    /// </summary>
    public IReadOnlyList<ParaViewStateProxy> Proxies { get; }

    /// <summary>
    /// Named proxy collections (registration names by id).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ParaViewStateCollectionItem>> Collections { get; }

    /// <summary>
    /// True when the state embeds custom (compound) proxy definitions — inadmissible in version 1.
    /// </summary>
    public bool HasCustomProxyDefinitions { get; }

    /// <summary>
    /// Registration names of the views, in collection order.
    /// </summary>
    public IReadOnlyList<string> ViewNames => CollectionItems(VIEWS_COLLECTION).Select(me => me.Name).ToList();

    /// <summary>
    /// The TimeKeeper timeline, or null when the state carries no TimeKeeper timestep values.
    /// </summary>
    public IReadOnlyList<double>? TimestepValues
    {
        get
        {
            var timeKeeper = Proxies.FirstOrDefault(me =>
                string.Equals(me.Group, TIME_KEEPER_GROUP, StringComparison.Ordinal)
                && string.Equals(me.Type, TIME_KEEPER_TYPE, StringComparison.Ordinal));

            var property = timeKeeper?.FindProperty(TIMESTEP_VALUES_PROPERTY);
            if (property == null)
                return null;

            var values = new List<double>(property.Values.Count);
            foreach (var value in property.Values)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    throw new ParaViewStateFormatException($"TimeKeeper timestep value '{value}' is not a number");

                values.Add(parsed);
            }

            return values;
        }
    }

    #endregion
}
