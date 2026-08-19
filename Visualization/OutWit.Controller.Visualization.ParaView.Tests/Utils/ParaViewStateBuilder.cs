using System.Globalization;
using System.Text;
using System.Xml;

namespace OutWit.Controller.Visualization.ParaView.Tests.Utils;

/// <summary>
/// Builds synthetic ParaView state (.pvsm) documents with the structure the controller parses:
/// ServerManagerState version, Proxy elements (group/type/id) with scalar Properties and Element
/// values, ProxyCollection registrations (views, sources) and the TimeKeeper timeline.
/// </summary>
internal sealed class ParaViewStateBuilder
{
    #region Fields

    private readonly List<string> m_proxies = [];

    private readonly Dictionary<string, List<(string Id, string Name)>> m_collections = new(StringComparer.Ordinal);

    private readonly List<double> m_timesteps = [];

    private int m_nextId = 1000;

    private string m_version = "6.1.1";

    private bool m_timeKeeper = true;

    private string m_extraRoot = string.Empty;

    private string m_prefix = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";

    #endregion

    #region Functions

    public ParaViewStateBuilder WithVersion(string version)
    {
        m_version = version;
        return this;
    }

    public ParaViewStateBuilder WithTimesteps(params double[] values)
    {
        m_timesteps.Clear();
        m_timesteps.AddRange(values);
        return this;
    }

    public ParaViewStateBuilder WithoutTimeKeeper()
    {
        m_timeKeeper = false;
        return this;
    }

    public ParaViewStateBuilder WithPrefix(string prefix)
    {
        m_prefix = prefix;
        return this;
    }

    public ParaViewStateBuilder WithExtraStateContent(string xml)
    {
        m_extraRoot += xml;
        return this;
    }

    /// <summary>Adds a reader proxy in the sources group with a FileName/FileNames property.</summary>
    public int AddReader(string type, string registrationName, params string[] fileNames)
    {
        var id = m_nextId++;
        var propertyName = fileNames.Length > 1 ? "FileNames" : "FileName";
        var elements = string.Concat(fileNames.Select((name, index) => $"      <Element index=\"{index}\" value=\"{Escape(name)}\"/>\n"));
        m_proxies.Add(
            $"  <Proxy group=\"sources\" type=\"{type}\" id=\"{id}\" servers=\"1\">\n" +
            $"    <Property name=\"{propertyName}\" id=\"{id}.{propertyName}\" number_of_elements=\"{fileNames.Length}\">\n{elements}" +
            $"      <Domain name=\"files\" id=\"{id}.{propertyName}.files\"/>\n" +
            "    </Property>\n" +
            $"    <Property name=\"TimestepValues\" id=\"{id}.TimestepValues\"/>\n" +
            "  </Proxy>\n");
        Register("sources", id.ToString(CultureInfo.InvariantCulture), registrationName);
        return id;
    }

    /// <summary>Adds a proxy of any group/type with arbitrary scalar properties (name → values).</summary>
    public int AddProxy(string group, string type, params (string Name, string[] Values)[] properties)
    {
        var id = m_nextId++;
        var builder = new StringBuilder();
        builder.Append($"  <Proxy group=\"{group}\" type=\"{type}\" id=\"{id}\" servers=\"21\">\n");
        foreach (var (name, values) in properties)
        {
            builder.Append($"    <Property name=\"{name}\" id=\"{id}.{name}\" number_of_elements=\"{values.Length}\">\n");
            for (var i = 0; i < values.Length; i++)
                builder.Append($"      <Element index=\"{i}\" value=\"{Escape(values[i])}\"/>\n");
            builder.Append("    </Property>\n");
        }

        builder.Append("  </Proxy>\n");
        m_proxies.Add(builder.ToString());
        return id;
    }

    /// <summary>Adds a filter in the sources group with an Input proxy property.</summary>
    public int AddFilter(string type, string registrationName, int inputId, params (string Name, string[] Values)[] properties)
    {
        var id = m_nextId++;
        var builder = new StringBuilder();
        builder.Append($"  <Proxy group=\"sources\" type=\"{type}\" id=\"{id}\" servers=\"1\">\n");
        builder.Append($"    <Property name=\"Input\" id=\"{id}.Input\" number_of_elements=\"1\">\n");
        builder.Append($"      <Proxy value=\"{inputId}\" output_port=\"0\"/>\n");
        builder.Append("    </Property>\n");
        foreach (var (name, values) in properties)
        {
            builder.Append($"    <Property name=\"{name}\" id=\"{id}.{name}\" number_of_elements=\"{values.Length}\">\n");
            for (var i = 0; i < values.Length; i++)
                builder.Append($"      <Element index=\"{i}\" value=\"{Escape(values[i])}\"/>\n");
            builder.Append("    </Property>\n");
        }

        builder.Append("  </Proxy>\n");
        m_proxies.Add(builder.ToString());
        Register("sources", id.ToString(CultureInfo.InvariantCulture), registrationName);
        return id;
    }

    /// <summary>Adds a render view registered under a name.</summary>
    public int AddRenderView(string registrationName = "RenderView1", int width = 1024, int height = 768)
    {
        var id = AddProxy("views", "RenderView",
            ("ViewSize", [width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture)]),
            ("ViewTime", ["0"]));
        Register("views", id.ToString(CultureInfo.InvariantCulture), registrationName);
        return id;
    }

    /// <summary>Adds a representation of a source in the representations group.</summary>
    public int AddRepresentation(string type, int inputId)
    {
        var id = m_nextId++;
        m_proxies.Add(
            $"  <Proxy group=\"representations\" type=\"{type}\" id=\"{id}\" servers=\"21\">\n" +
            $"    <Property name=\"Input\" id=\"{id}.Input\" number_of_elements=\"1\">\n" +
            $"      <Proxy value=\"{inputId}\" output_port=\"0\"/>\n" +
            "    </Property>\n" +
            $"    <Property name=\"Representation\" id=\"{id}.Representation\" number_of_elements=\"1\">\n" +
            "      <Element index=\"0\" value=\"Surface\"/>\n" +
            "    </Property>\n" +
            "  </Proxy>\n");
        Register("representations", id.ToString(CultureInfo.InvariantCulture), $"{type}{id}");
        return id;
    }

    public void Register(string collection, string id, string name)
    {
        if (!m_collections.TryGetValue(collection, out var items))
        {
            items = [];
            m_collections[collection] = items;
        }

        items.Add((id, name));
    }

    /// <summary>A complete, allowlist-conformant state: one XML unstructured grid reader, a contour, a view, the timeline.</summary>
    public static ParaViewStateBuilder Typical(params string[] fileNames)
    {
        var builder = new ParaViewStateBuilder();
        var reader = builder.AddReader("XMLUnstructuredGridReader", "field.vtu", fileNames.Length == 0 ? ["data/field.vtu"] : fileNames);
        var contour = builder.AddFilter("Contour", "Contour1", reader, ("ContourValues", ["0.5"]));
        builder.AddRepresentation("UnstructuredGridRepresentation", reader);
        builder.AddRepresentation("GeometryRepresentation", contour);
        builder.AddRenderView();
        builder.AddProxy("lookup_tables", "PVLookupTable", ("ColorSpace", ["Diverging"]));
        builder.AddProxy("animation", "AnimationScene", ("PlayMode", ["Snap To TimeSteps"]));
        return builder;
    }

    public string Build()
    {
        var builder = new StringBuilder();
        builder.Append(m_prefix);
        builder.Append("<ParaView>\n");
        builder.Append($"<ServerManagerState version=\"{m_version}\">\n");

        foreach (var proxy in m_proxies)
            builder.Append(proxy);

        if (m_timeKeeper)
        {
            var id = m_nextId++;
            builder.Append($"  <Proxy group=\"misc\" type=\"TimeKeeper\" id=\"{id}\" servers=\"16\">\n");
            builder.Append($"    <Property name=\"Time\" id=\"{id}.Time\" number_of_elements=\"1\">\n      <Element index=\"0\" value=\"0\"/>\n    </Property>\n");
            builder.Append($"    <Property name=\"TimestepValues\" id=\"{id}.TimestepValues\" number_of_elements=\"{m_timesteps.Count}\">\n");
            for (var i = 0; i < m_timesteps.Count; i++)
                builder.Append($"      <Element index=\"{i}\" value=\"{m_timesteps[i].ToString("R", CultureInfo.InvariantCulture)}\"/>\n");
            builder.Append("    </Property>\n");
            builder.Append("  </Proxy>\n");
            Register("timekeeper", id.ToString(CultureInfo.InvariantCulture), "TimeKeeper1");
        }

        foreach (var (collection, items) in m_collections)
        {
            builder.Append($"  <ProxyCollection name=\"{collection}\">\n");
            foreach (var (id, name) in items)
                builder.Append($"    <Item id=\"{id}\" name=\"{Escape(name)}\"/>\n");
            builder.Append("  </ProxyCollection>\n");
        }

        builder.Append(m_extraRoot);
        builder.Append("</ServerManagerState>\n");
        builder.Append("</ParaView>\n");
        return builder.ToString();
    }

    public string WriteTo(string path)
    {
        File.WriteAllText(path, Build(), new UTF8Encoding(false));
        return path;
    }

    #endregion

    #region Tools

    private static string Escape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }

    #endregion
}
