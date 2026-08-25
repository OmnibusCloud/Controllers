using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OutWit.Controller.Visualization.ParaView.Tests.FakePvpython;

/// <summary>
/// The fake's compose mode: selected when the script passed before <c>--task-file</c> is
/// <c>compose_scene.py</c>. Honours the composer contract: reads the snake_case compose task, checks
/// the process contract (cwd = package root, no DISPLAY, the data file materialized under the package
/// root), writes a state that the REAL host validator accepts — the bundled reader on the data's
/// LOGICAL path, a representation, a render view, a lookup table, the animation scene and a
/// TimeKeeper carrying the timeline it reports — and the snake_case status document on every exit
/// path, exit code forwarded.
///
/// Behaviours are driven by markers in the DATA file text:
///   FAKE-COMPOSE-FAIL        fails at inspect: status ok=false, exit 3, no state;
///   FAKE-COMPOSE-HANG        sleeps like a wedged composer — the cancellation gates kill the tree;
///   FAKE-COMPOSE-NO-STATE    reports success but saves no state (the executor must reject);
///   FAKE-COMPOSE-BAD-PROXY   saves a state with a proxy the allowlist refuses (the validator guard must reject);
///   FAKE-COMPOSE-ABSOLUTE    saves a state referencing the data by its ABSOLUTE path (a composer that
///                            forgot to rewrite — the validator guard must reject);
///   FAKE-TIMESTEPS=N         the reported timeline has N steps (default 3);
///   anything else            a normal composition.
/// A colour array the fake does not carry (it offers NDTEMP and DISP point arrays, no cell arrays)
/// fails exactly like the real composer: exit 3 naming the arrays that exist.
/// </summary>
internal static class FakeCompose
{
    #region Constants

    public const string SCRIPT_NAME = "compose_scene.py";

    public const string MARKER_FAIL = "FAKE-COMPOSE-FAIL";

    public const string MARKER_HANG = "FAKE-COMPOSE-HANG";

    public const string MARKER_NO_STATE = "FAKE-COMPOSE-NO-STATE";

    public const string MARKER_BAD_PROXY = "FAKE-COMPOSE-BAD-PROXY";

    public const string MARKER_ABSOLUTE = "FAKE-COMPOSE-ABSOLUTE";

    public const string MARKER_TIMESTEPS = "FAKE-TIMESTEPS=";

    public const string PARAVIEW_VERSION = "6.1.1-fake";

    public const int DEFAULT_TIMESTEPS = 3;

    private static readonly string[] POINT_ARRAYS = ["NDTEMP", "DISP"];

    #endregion

    #region Functions

    public static int Run(JsonElement task)
    {
        string Str(string name) => task.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        int Int(string name, int fallback) => task.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;
        bool Bool(string name) => task.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        var statusPath = Str("status_path");
        var status = new Dictionary<string, object?>
        {
            ["schema"] = 1,
            ["ok"] = false,
            ["stage"] = "load-task",
            ["error"] = "",
            ["paraview_version"] = PARAVIEW_VERSION,
            ["reader_version"] = "",
            ["timestep_values"] = new List<double>(),
            ["point_arrays"] = new List<string>(),
            ["cell_arrays"] = new List<string>(),
            ["color_array"] = "",
            ["color_association"] = "",
            ["color_range"] = new List<double>(),
            ["bounds"] = new List<double>(),
            ["fit_samples"] = 0,
            ["state_bytes"] = 0L,
            ["compose_seconds"] = 0.0
        };

        void WriteStatus()
        {
            if (!string.IsNullOrEmpty(statusPath))
                File.WriteAllText(statusPath, JsonSerializer.Serialize(status));
        }

        int Fail(string stage, string error, int exitCode)
        {
            status["stage"] = stage;
            status["error"] = error;
            WriteStatus();
            Console.Error.WriteLine($"fake-pvpython compose: {error}");
            return exitCode;
        }

        if (Int("schema", 0) != 1)
            return Fail("load-task", "unsupported compose task schema", 2);

        var packageRoot = Str("package_root");
        var statePath = Str("state_path");
        var dataPath = Str("data_path");
        var logicalPath = Str("data_logical_path");
        var registrationName = Str("registration_name");
        var pluginPath = Str("plugin_path");
        var colorArrayName = Str("color_array_name");
        var colorAssociation = Str("color_association");
        var representation = Str("representation");
        var fitTo = Str("fit_to");
        var viewWidth = Int("view_width", 1920);
        var viewHeight = Int("view_height", 1080);
        var showScalarBar = Bool("show_scalar_bar");

        if (!File.Exists(dataPath))
            return Fail("load-task", $"data file '{dataPath}' does not exist", 2);

        if (!string.Equals(Path.GetFullPath(Directory.GetCurrentDirectory()), Path.GetFullPath(packageRoot), StringComparison.OrdinalIgnoreCase))
            return Fail("load-task", "cwd is not the package root", 3);

        if (Environment.GetEnvironmentVariable("DISPLAY") != null)
            return Fail("load-task", "DISPLAY leaked into the composer environment", 3);

        if (File.Exists(statePath))
            return Fail("load-task", $"state path '{statePath}' already exists", 3);

        status["reader_version"] = ReadReaderVersion(pluginPath);

        var dataText = File.ReadAllText(dataPath);

        if (dataText.Contains(MARKER_FAIL, StringComparison.Ordinal))
            return Fail("inspect", "fake failure requested by the data", 3);

        if (dataText.Contains(MARKER_HANG, StringComparison.Ordinal))
        {
            status["stage"] = "inspect";
            WriteStatus();
            Thread.Sleep(TimeSpan.FromMinutes(5));
            return 0;
        }

        var timesteps = Timeline(dataText);
        status["stage"] = "inspect";
        status["point_arrays"] = POINT_ARRAYS.ToList();
        status["cell_arrays"] = new List<string>();

        string colorArray;
        if (colorArrayName.Length > 0)
        {
            var available = string.Equals(colorAssociation, "POINTS", StringComparison.Ordinal) ? POINT_ARRAYS : [];
            if (!available.Contains(colorArrayName, StringComparer.Ordinal))
                return Fail("inspect", $"'{logicalPath}' carries no {colorAssociation.ToLowerInvariant()} array '{colorArrayName}' (point arrays: {string.Join(", ", POINT_ARRAYS)}; cell arrays: none)", 3);
            colorArray = colorArrayName;
        }
        else
        {
            colorArray = POINT_ARRAYS[0];
            colorAssociation = "POINTS";
        }

        status["stage"] = "present";
        status["color_array"] = colorArray;
        status["color_association"] = colorAssociation;

        status["stage"] = "fit";
        var samples = fitTo switch
        {
            "first" => 1,
            "last" => 1,
            _ => timesteps.Count
        };
        status["fit_samples"] = samples;
        status["bounds"] = new List<double> { 0, 1, 0, 1, 0, 1 };
        status["color_range"] = new List<double> { 0, 100 };
        status["timestep_values"] = timesteps;

        status["stage"] = "save-state";
        if (dataText.Contains(MARKER_NO_STATE, StringComparison.Ordinal))
        {
            status["stage"] = "done";
            status["ok"] = true;
            WriteStatus();
            return 0;
        }

        var referencedPath = dataText.Contains(MARKER_ABSOLUTE, StringComparison.Ordinal) ? dataPath : logicalPath;
        var badProxy = dataText.Contains(MARKER_BAD_PROXY, StringComparison.Ordinal);

        try
        {
            File.WriteAllText(statePath, BuildState(referencedPath, registrationName, timesteps, representation, colorArray, viewWidth, viewHeight, showScalarBar, badProxy), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            return Fail("save-state", $"cannot save the state: {e.Message}", 1);
        }

        status["stage"] = "done";
        status["ok"] = true;
        status["state_bytes"] = new FileInfo(statePath).Length;
        status["compose_seconds"] = 0.01;
        WriteStatus();
        return 0;
    }

    #endregion

    #region Tools

    private static List<double> Timeline(string dataText)
    {
        var count = DEFAULT_TIMESTEPS;
        var marker = dataText.IndexOf(MARKER_TIMESTEPS, StringComparison.Ordinal);
        if (marker >= 0)
        {
            var digits = new string(dataText[(marker + MARKER_TIMESTEPS.Length)..].TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                count = parsed;
        }

        return Enumerable.Range(1, count).Select(index => index * 1.0).ToList();
    }

    private static string ReadReaderVersion(string pluginPath)
    {
        try
        {
            foreach (var line in File.ReadLines(pluginPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("__version__", StringComparison.Ordinal))
                    continue;

                var separator = trimmed.IndexOf('=');
                return separator < 0 ? string.Empty : trimmed[(separator + 1)..].Trim().Trim('"', '\'');
            }
        }
        catch
        {
            // absent plugin: the status carries no reader version, like the real composer
        }

        return string.Empty;
    }

    private static string BuildState(string referencedPath, string registrationName, IReadOnlyList<double> timesteps, string representation, string colorArray, int viewWidth, int viewHeight, bool showScalarBar, bool badProxy)
    {
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.Append("<ParaView>\n<ServerManagerState version=\"6.1.1\">\n");

        builder.Append("  <Proxy group=\"sources\" type=\"OmnibusCloudFrdReader\" id=\"1\" servers=\"1\">\n");
        builder.Append("    <Property name=\"FileName\" id=\"1.FileName\" number_of_elements=\"1\">\n");
        builder.Append($"      <Element index=\"0\" value=\"{Escape(referencedPath)}\"/>\n");
        builder.Append("      <Domain name=\"files\" id=\"1.FileName.files\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append($"    <Property name=\"TimestepValues\" id=\"1.TimestepValues\" number_of_elements=\"{timesteps.Count}\">\n");
        for (var i = 0; i < timesteps.Count; i++)
            builder.Append($"      <Element index=\"{i}\" value=\"{Number(timesteps[i])}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("  </Proxy>\n");

        if (badProxy)
        {
            builder.Append("  <Proxy group=\"sources\" type=\"ProgrammableSource\" id=\"9\" servers=\"1\">\n");
            builder.Append("    <Property name=\"Script\" id=\"9.Script\" number_of_elements=\"1\">\n");
            builder.Append("      <Element index=\"0\" value=\"print(1)\"/>\n");
            builder.Append("    </Property>\n");
            builder.Append("  </Proxy>\n");
        }

        builder.Append("  <Proxy group=\"representations\" type=\"UnstructuredGridRepresentation\" id=\"2\" servers=\"21\">\n");
        builder.Append("    <Property name=\"Input\" id=\"2.Input\" number_of_elements=\"1\">\n");
        builder.Append("      <Proxy value=\"1\" output_port=\"0\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"Representation\" id=\"2.Representation\" number_of_elements=\"1\">\n");
        builder.Append($"      <Element index=\"0\" value=\"{Escape(representation)}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"ColorArrayName\" id=\"2.ColorArrayName\" number_of_elements=\"5\">\n");
        builder.Append("      <Element index=\"0\" value=\"\"/>\n      <Element index=\"1\" value=\"\"/>\n      <Element index=\"2\" value=\"\"/>\n      <Element index=\"3\" value=\"0\"/>\n");
        builder.Append($"      <Element index=\"4\" value=\"{Escape(colorArray)}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("  </Proxy>\n");

        builder.Append("  <Proxy group=\"views\" type=\"RenderView\" id=\"3\" servers=\"21\">\n");
        builder.Append("    <Property name=\"ViewSize\" id=\"3.ViewSize\" number_of_elements=\"2\">\n");
        builder.Append($"      <Element index=\"0\" value=\"{viewWidth}\"/>\n      <Element index=\"1\" value=\"{viewHeight}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"ViewTime\" id=\"3.ViewTime\" number_of_elements=\"1\">\n");
        builder.Append($"      <Element index=\"0\" value=\"{Number(timesteps.Count == 0 ? 0.0 : timesteps[^1])}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"CameraPosition\" id=\"3.CameraPosition\" number_of_elements=\"3\">\n");
        builder.Append("      <Element index=\"0\" value=\"3\"/>\n      <Element index=\"1\" value=\"3\"/>\n      <Element index=\"2\" value=\"3\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"CameraFocalPoint\" id=\"3.CameraFocalPoint\" number_of_elements=\"3\">\n");
        builder.Append("      <Element index=\"0\" value=\"0.5\"/>\n      <Element index=\"1\" value=\"0.5\"/>\n      <Element index=\"2\" value=\"0.5\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"CameraViewUp\" id=\"3.CameraViewUp\" number_of_elements=\"3\">\n");
        builder.Append("      <Element index=\"0\" value=\"0\"/>\n      <Element index=\"1\" value=\"0\"/>\n      <Element index=\"2\" value=\"1\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("  </Proxy>\n");

        builder.Append("  <Proxy group=\"lookup_tables\" type=\"PVLookupTable\" id=\"4\" servers=\"21\">\n");
        builder.Append("    <Property name=\"ColorSpace\" id=\"4.ColorSpace\" number_of_elements=\"1\">\n");
        builder.Append("      <Element index=\"0\" value=\"Diverging\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("    <Property name=\"AutomaticRescaleRangeMode\" id=\"4.AutomaticRescaleRangeMode\" number_of_elements=\"1\">\n");
        builder.Append("      <Element index=\"0\" value=\"Never\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("  </Proxy>\n");

        if (showScalarBar)
        {
            builder.Append("  <Proxy group=\"representations\" type=\"ScalarBarWidgetRepresentation\" id=\"7\" servers=\"21\">\n");
            builder.Append("    <Property name=\"Title\" id=\"7.Title\" number_of_elements=\"1\">\n");
            builder.Append($"      <Element index=\"0\" value=\"{Escape(colorArray)}\"/>\n");
            builder.Append("    </Property>\n");
            builder.Append("  </Proxy>\n");
        }

        builder.Append("  <Proxy group=\"animation\" type=\"AnimationScene\" id=\"5\" servers=\"16\">\n");
        builder.Append("    <Property name=\"PlayMode\" id=\"5.PlayMode\" number_of_elements=\"1\">\n");
        builder.Append("      <Element index=\"0\" value=\"Snap To TimeSteps\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("  </Proxy>\n");

        builder.Append("  <Proxy group=\"misc\" type=\"TimeKeeper\" id=\"6\" servers=\"16\">\n");
        builder.Append("    <Property name=\"Time\" id=\"6.Time\" number_of_elements=\"1\">\n");
        builder.Append($"      <Element index=\"0\" value=\"{Number(timesteps.Count == 0 ? 0.0 : timesteps[^1])}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append($"    <Property name=\"TimestepValues\" id=\"6.TimestepValues\" number_of_elements=\"{timesteps.Count}\">\n");
        for (var i = 0; i < timesteps.Count; i++)
            builder.Append($"      <Element index=\"{i}\" value=\"{Number(timesteps[i])}\"/>\n");
        builder.Append("    </Property>\n");
        builder.Append("  </Proxy>\n");

        builder.Append("  <ProxyCollection name=\"sources\">\n");
        builder.Append($"    <Item id=\"1\" name=\"{Escape(registrationName)}\"/>\n");
        if (badProxy)
            builder.Append("    <Item id=\"9\" name=\"ProgrammableSource1\"/>\n");
        builder.Append("  </ProxyCollection>\n");
        builder.Append("  <ProxyCollection name=\"representations\">\n    <Item id=\"2\" name=\"UnstructuredGridRepresentation1\"/>\n");
        if (showScalarBar)
            builder.Append("    <Item id=\"7\" name=\"ScalarBarWidgetRepresentation1\"/>\n");
        builder.Append("  </ProxyCollection>\n");
        builder.Append("  <ProxyCollection name=\"views\">\n    <Item id=\"3\" name=\"RenderView1\"/>\n  </ProxyCollection>\n");
        builder.Append($"  <ProxyCollection name=\"lookup_tables\">\n    <Item id=\"4\" name=\"{Escape(colorArray)}.PVLookupTable\"/>\n  </ProxyCollection>\n");
        builder.Append("  <ProxyCollection name=\"animation\">\n    <Item id=\"5\" name=\"AnimationScene1\"/>\n  </ProxyCollection>\n");
        builder.Append("  <ProxyCollection name=\"timekeeper\">\n    <Item id=\"6\" name=\"TimeKeeper1\"/>\n  </ProxyCollection>\n");

        builder.Append("</ServerManagerState>\n</ParaView>\n");
        return builder.ToString();
    }

    private static string Escape(string value)
    {
        return System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }

    #endregion
}
