using System.Globalization;
using System.Xml;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Streaming parser behind <see cref="ParaViewStateDocument"/>: walks the hardened XmlReader once,
/// enforcing depth/element/attribute/text bounds on every node, and extracts proxies (with their
/// scalar properties and files-domain marks), proxy collections and custom-definition presence.
/// </summary>
internal sealed class ParaViewStateDocumentParser
{
    #region Constants

    private const string ROOT_ELEMENT = "ParaView";

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

    public ParaViewStateDocument Parse()
    {
        if (!ReadToElement() || m_reader.Name != ROOT_ELEMENT)
            throw new ParaViewStateFormatException($"state root element must be <{ROOT_ELEMENT}>");

        var rootDepth = m_reader.Depth;

        while (m_reader.Read())
        {
            Guard();

            if (m_reader.NodeType != XmlNodeType.Element)
                continue;

            if (m_reader.Depth == rootDepth + 1 && m_reader.Name == STATE_ELEMENT)
            {
                m_version = m_reader.GetAttribute("version") ?? string.Empty;
                ParseState(m_reader.Depth);
            }
        }

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
                    m_hasCustomDefinitions = true;
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
