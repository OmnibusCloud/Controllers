using System.Globalization;
using System.Xml;
using OutWit.Controller.Visualization.ParaView.Validation;

namespace OutWit.Controller.Visualization.ParaView.State;

/// <summary>
/// Streaming parser behind <see cref="ParaViewStateDocument"/>: walks the hardened XmlReader once,
/// enforcing depth/element/attribute/text bounds on every node, and extracts proxies (with their
/// scalar properties and files-domain marks), proxy collections and custom-definition presence.
/// </summary>
internal sealed class ParaViewStateDocumentParser
{
    #region Constants

    private const string STATE_ELEMENT = "ServerManagerState";

    private const string PROXY_ELEMENT = "Proxy";

    private const string PROPERTY_ELEMENT = "Property";

    private const string ELEMENT_ELEMENT = "Element";

    private const string DOMAIN_ELEMENT = "Domain";

    private const string FILES_DOMAIN = "files";

    private const string COLLECTION_ELEMENT = "ProxyCollection";

    private const string ITEM_ELEMENT = "Item";

    private const string CUSTOM_DEFINITIONS_ELEMENT = "CustomProxyDefinitions";

    #endregion

    #region Fields

    private readonly XmlReader m_reader;

    private readonly List<ParaViewStateProxy> m_proxies = [];

    private readonly Dictionary<string, IReadOnlyList<ParaViewStateCollectionItem>> m_collections = new(StringComparer.Ordinal);

    private int m_elements;

    private int m_attributes;

    private bool m_hasCustomDefinitions;

    private string m_version = string.Empty;

    #endregion

    #region Constructors

    public ParaViewStateDocumentParser(XmlReader reader)
    {
        m_reader = reader;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Walks the whole document once and builds the parsed state.
    /// </summary>
    /// <returns>The parsed document.</returns>
    /// <exception cref="ParaViewStateFormatException">A structural limit or a prohibited construct was hit.</exception>
    public ParaViewStateDocument Parse()
    {
        // The GUI saves <ParaView>, pvpython saves <GenericParaViewApplication>: the contract is the
        // ServerManagerState child, not the root tag.
        if (!ReadToElement())
            throw new ParaViewStateFormatException("state has no root element");

        var rootDepth = m_reader.Depth;
        var stateSeen = false;

        while (m_reader.Read())
        {
            Guard();

            if (m_reader.NodeType != XmlNodeType.Element)
                continue;

            if (m_reader.Depth == rootDepth + 1 && m_reader.Name == STATE_ELEMENT)
            {
                stateSeen = true;
                m_version = m_reader.GetAttribute("version") ?? string.Empty;
                ParseState(m_reader.Depth);
            }
        }

        if (!stateSeen)
            throw new ParaViewStateFormatException($"state carries no <{STATE_ELEMENT}> element");

        return new ParaViewStateDocument(m_version, m_proxies, m_collections, m_hasCustomDefinitions);
    }

    private void ParseState(int stateDepth)
    {
        if (m_reader.IsEmptyElement)
            return;

        while (m_reader.Read())
        {
            Guard();

            if (m_reader.NodeType == XmlNodeType.EndElement && m_reader.Depth == stateDepth)
                return;

            if (m_reader.NodeType != XmlNodeType.Element || m_reader.Depth != stateDepth + 1)
                continue;

            switch (m_reader.Name)
            {
                case PROXY_ELEMENT:
                    ParseProxy(m_reader.Depth);
                    break;

                case COLLECTION_ELEMENT:
                    ParseCollection(m_reader.Depth);
                    break;

                case CUSTOM_DEFINITIONS_ELEMENT:
                    // Every saved state carries an empty <CustomProxyDefinitions/>; only a populated one embeds definitions.
                    if (!m_reader.IsEmptyElement)
                        m_hasCustomDefinitions = HasChildElements(m_reader.Depth);
                    break;
            }
        }
    }

    private void ParseProxy(int proxyDepth)
    {
        var group = m_reader.GetAttribute("group") ?? string.Empty;
        var type = m_reader.GetAttribute("type") ?? string.Empty;
        var id = m_reader.GetAttribute("id") ?? string.Empty;
        var properties = new List<ParaViewStateProperty>();

        if (!m_reader.IsEmptyElement)
        {
            while (m_reader.Read())
            {
                Guard();

                if (m_reader.NodeType == XmlNodeType.EndElement && m_reader.Depth == proxyDepth)
                    break;

                if (m_reader.NodeType != XmlNodeType.Element || m_reader.Depth != proxyDepth + 1)
                    continue;

                if (m_reader.Name == PROPERTY_ELEMENT)
                    properties.Add(ParseProperty(m_reader.Depth));
            }
        }

        m_proxies.Add(new ParaViewStateProxy(group, type, id, properties));
    }

    private ParaViewStateProperty ParseProperty(int propertyDepth)
    {
        var name = m_reader.GetAttribute("name") ?? string.Empty;
        var values = new List<(int Index, string Value)>();
        var hasFileDomain = false;

        if (!m_reader.IsEmptyElement)
        {
            while (m_reader.Read())
            {
                Guard();

                if (m_reader.NodeType == XmlNodeType.EndElement && m_reader.Depth == propertyDepth)
                    break;

                if (m_reader.NodeType != XmlNodeType.Element || m_reader.Depth != propertyDepth + 1)
                    continue;

                if (m_reader.Name == DOMAIN_ELEMENT)
                {
                    if (string.Equals(m_reader.GetAttribute("name"), FILES_DOMAIN, StringComparison.Ordinal))
                        hasFileDomain = true;
                    continue;
                }

                if (m_reader.Name != ELEMENT_ELEMENT)
                    continue;

                var indexText = m_reader.GetAttribute("index");
                var index = int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : values.Count;
                values.Add((index, m_reader.GetAttribute("value") ?? string.Empty));
            }
        }

        return new ParaViewStateProperty(name, values.OrderBy(me => me.Index).Select(me => me.Value).ToList(), hasFileDomain);
    }

    private void ParseCollection(int collectionDepth)
    {
        var name = m_reader.GetAttribute("name") ?? string.Empty;
        var items = new List<ParaViewStateCollectionItem>();

        if (!m_reader.IsEmptyElement)
        {
            while (m_reader.Read())
            {
                Guard();

                if (m_reader.NodeType == XmlNodeType.EndElement && m_reader.Depth == collectionDepth)
                    break;

                if (m_reader.NodeType != XmlNodeType.Element || m_reader.Depth != collectionDepth + 1)
                    continue;

                if (m_reader.Name == ITEM_ELEMENT)
                    items.Add(new ParaViewStateCollectionItem(m_reader.GetAttribute("id") ?? string.Empty, m_reader.GetAttribute("name") ?? string.Empty));
            }
        }

        m_collections[name] = items;
    }

    private bool HasChildElements(int depth)
    {
        var found = false;
        while (m_reader.Read())
        {
            Guard();

            if (m_reader.NodeType == XmlNodeType.EndElement && m_reader.Depth == depth)
                break;

            if (m_reader.NodeType == XmlNodeType.Element)
                found = true;
        }

        return found;
    }

    private bool ReadToElement()
    {
        while (m_reader.Read())
        {
            Guard();

            if (m_reader.NodeType == XmlNodeType.Element)
                return true;
        }

        return false;
    }

    private void Guard()
    {
        if (m_reader.Depth > ParaViewInputLimits.MAX_XML_DEPTH)
            throw new ParaViewStateFormatException($"state XML nests deeper than {ParaViewInputLimits.MAX_XML_DEPTH} levels");

        switch (m_reader.NodeType)
        {
            case XmlNodeType.Element:
                if (++m_elements > ParaViewInputLimits.MAX_XML_ELEMENTS)
                    throw new ParaViewStateFormatException($"state XML has more than {ParaViewInputLimits.MAX_XML_ELEMENTS} elements");

                m_attributes += m_reader.AttributeCount;
                if (m_attributes > ParaViewInputLimits.MAX_XML_ATTRIBUTES)
                    throw new ParaViewStateFormatException($"state XML has more than {ParaViewInputLimits.MAX_XML_ATTRIBUTES} attributes");

                if (m_reader.HasAttributes)
                {
                    for (var i = 0; i < m_reader.AttributeCount; i++)
                    {
                        m_reader.MoveToAttribute(i);
                        if (m_reader.Value.Length > ParaViewInputLimits.MAX_XML_TEXT_CHARS)
                            throw new ParaViewStateFormatException($"state XML attribute '{m_reader.Name}' exceeds {ParaViewInputLimits.MAX_XML_TEXT_CHARS} characters");
                    }

                    m_reader.MoveToElement();
                }

                break;

            case XmlNodeType.Text:
            case XmlNodeType.CDATA:
                if (m_reader.Value.Length > ParaViewInputLimits.MAX_XML_TEXT_CHARS)
                    throw new ParaViewStateFormatException($"state XML text node exceeds {ParaViewInputLimits.MAX_XML_TEXT_CHARS} characters");

                break;

            case XmlNodeType.DocumentType:
            case XmlNodeType.EntityReference:
            case XmlNodeType.Entity:
                throw new ParaViewStateFormatException("state XML must not declare or reference entities");
        }
    }

    #endregion
}
