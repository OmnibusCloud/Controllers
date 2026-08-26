// Fake pvpython for the SDK end-to-end tests. Honors the host contract the real runtime is
// invoked with:
//   fake-pvpython [pvpython options] <render_task.py> --task-file <task.json>
//   fake-pvpython --version
// and the runner's document contract (schema 2): reads the snake_case task file — the shared
// state/view/size/format plus the OUTPUTS list — writes the snake_case status file (per-output
// verdicts included) on every exit path, renders every output into its own path, exit code forwarded.
//
// Like the real runner it parses the state and resolves every file reference under the package
// root: a single-valued reference must be a materialized package file, a multi-valued one (a file
// series) must have at least one — the e2e proof that a task's attachment subset suffices.
// Behaviours are driven by markers in the STATE text:
//   FAKE-FAIL            fails at load-state: status ok=false, exit 3, no output;
//   FAKE-HANG            sleeps like a wedged runner — the cancellation gates kill the tree;
//   FAKE-WRONG-SIZE      renders one pixel wider than requested (output validation must reject);
//   FAKE-NO-STATUS       renders, exits 0, writes no status (adapter must reject);
//   FAKE-EXTRA-OUTPUT    renders and leaves an extra file in the output directory (must reject);
//   FAKE-EGL-CRASH       dies like a segfault (exit 1, no status) when VTK_DEFAULT_OPENGL_WINDOW=vtkEGLRenderWindow;
//   FAKE-FAIL-OUTPUT-<n> renders the outputs before index n, fails output n at render (status
//                        outputs[n].ok=false, task ok=false, exit 1) — a batch is all-or-nothing;
//   anything else        renders a solid image of the requested size per output.
//
// When the script is benchmark_frames.py the fake runs its benchmark mode instead (see FakeBenchmark).
// When the script is gpu_probe.py the fake runs its GPU-probe mode (see FakeGpuProbe).
// When the script is compose_scene.py the fake runs its compose mode (see FakeCompose).

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OutWit.Controller.Visualization.ParaView.Tests.FakePvpython;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine("paraview version 6.1.1");
    return 0;
}

var probeScript = args.FirstOrDefault(arg => arg.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(arg).Equals(FakeGpuProbe.SCRIPT_NAME, StringComparison.OrdinalIgnoreCase));
if (probeScript != null)
    return FakeGpuProbe.Run(args);

var taskFileIndex = Array.IndexOf(args, "--task-file");
if (taskFileIndex < 0 || taskFileIndex + 1 >= args.Length)
{
    Console.Error.WriteLine("fake-pvpython: expected --task-file <path>");
    return 2;
}

var scriptPath = args.Take(taskFileIndex).LastOrDefault(arg => arg.EndsWith(".py", StringComparison.OrdinalIgnoreCase));
if (scriptPath == null || !File.Exists(scriptPath))
{
    Console.Error.WriteLine("fake-pvpython: the runner script was not passed before --task-file or does not exist");
    return 2;
}

JsonElement task;
try
{
    task = JsonDocument.Parse(File.ReadAllText(args[taskFileIndex + 1])).RootElement;
}
catch (Exception e)
{
    Console.Error.WriteLine($"fake-pvpython: cannot read the task file: {e.Message}");
    return 2;
}

if (string.Equals(Path.GetFileName(scriptPath), FakeBenchmark.SCRIPT_NAME, StringComparison.OrdinalIgnoreCase))
    return FakeBenchmark.Run(task);

if (string.Equals(Path.GetFileName(scriptPath), FakeCompose.SCRIPT_NAME, StringComparison.OrdinalIgnoreCase))
    return FakeCompose.Run(task);

string Str(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
int Int(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
HashSet<string> Set(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
    ? value.EnumerateArray().Select(me => me.GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal)
    : [];

var statusPath = Str(task, "status_path");
var outputStatuses = new List<Dictionary<string, object?>>();
var status = new Dictionary<string, object?>
{
    ["schema"] = 2,
    ["ok"] = false,
    ["stage"] = "load-task",
    ["error"] = "",
    ["paraview_version"] = "6.1.1-fake",
    ["reader_version"] = "",
    ["proxy_count"] = 0,
    ["render_seconds"] = 0.0,
    ["width"] = 0,
    ["height"] = 0,
    ["backend"] = "FakeOffscreenWindow",
    ["outputs"] = outputStatuses
};

int Fail(string stage, string error, int exitCode)
{
    status["stage"] = stage;
    status["error"] = error;
    WriteStatus();
    Console.Error.WriteLine($"fake-pvpython: {error}");
    return exitCode;
}

void WriteStatus()
{
    if (!string.IsNullOrEmpty(statusPath))
        File.WriteAllText(statusPath, JsonSerializer.Serialize(status));
}

if (Int(task, "schema") != 2)
    return Fail("load-task", "unsupported task schema", 2);

var statePath = Str(task, "state_path");
var packageRoot = Str(task, "package_root");
var width = Int(task, "width");
var height = Int(task, "height");
var format = Str(task, "format");
var transparent = Bool(task, "transparent_background");
var fileReferenceGroups = Set(task, "file_reference_groups");
var filePropertyNames = Set(task, "file_property_names");

var outputs = task.TryGetProperty("outputs", out var outputsElement) && outputsElement.ValueKind == JsonValueKind.Array
    ? outputsElement.EnumerateArray().ToList()
    : [];
if (outputs.Count == 0)
    return Fail("load-task", "task file lists no outputs", 2);

if (!File.Exists(statePath))
    return Fail("load-task", $"state file '{statePath}' does not exist", 2);

if (!string.Equals(Path.GetFullPath(Directory.GetCurrentDirectory()), Path.GetFullPath(packageRoot), StringComparison.OrdinalIgnoreCase))
    return Fail("load-task", "cwd is not the package root", 3);

if (Environment.GetEnvironmentVariable("DISPLAY") != null)
    return Fail("load-task", "DISPLAY leaked into the runner environment", 3);

var stateText = File.ReadAllText(statePath);

if (stateText.Contains("FAKE-FAIL", StringComparison.Ordinal) && !stateText.Contains("FAKE-FAIL-OUTPUT", StringComparison.Ordinal))
    return Fail("load-state", "fake failure requested by the state", 3);

// A flaky EGL stack: dies like a segfault (no status at all) ONLY when the EGL window is requested.
if (stateText.Contains("FAKE-EGL-CRASH", StringComparison.Ordinal)
    && Environment.GetEnvironmentVariable("VTK_DEFAULT_OPENGL_WINDOW") == "vtkEGLRenderWindow")
{
    Console.Error.WriteLine("error: exception occurred: Segmentation fault");
    return 1;
}

if (stateText.Contains("FAKE-HANG", StringComparison.Ordinal))
{
    status["stage"] = "render";
    WriteStatus();
    Thread.Sleep(TimeSpan.FromMinutes(5));
    return 0;
}

// Resolve every file reference the real runner would: it must be a logical path the task
// materialized under the package root.
status["stage"] = "resolve-state";
var proxyCount = 0;
try
{
    var document = XDocument.Parse(stateText);
    foreach (var proxy in document.Descendants("Proxy"))
    {
        proxyCount++;
        var group = (string?)proxy.Attribute("group") ?? string.Empty;
        if (!fileReferenceGroups.Contains(group))
            continue;

        foreach (var property in proxy.Elements("Property"))
        {
            var name = (string?)property.Attribute("name") ?? string.Empty;
            var values = property.Elements("Element").Select(me => (string?)me.Attribute("value") ?? string.Empty).ToList();
            var hasFileDomain = property.Elements("Domain").Any(me => (string?)me.Attribute("name") == "files");
            var isFile = hasFileDomain
                         || filePropertyNames.Contains(name)
                         || name.EndsWith("FileName", StringComparison.Ordinal)
                         || name.EndsWith("FileNames", StringComparison.Ordinal);
            if (!isFile)
                continue;

            var referenced = values.Where(me => me.Length > 0).ToList();
            var present = referenced.Count(me => File.Exists(Path.Combine(packageRoot, me.Replace('/', Path.DirectorySeparatorChar))));
            if (referenced.Count > 0 && present == 0)
                return Fail("resolve-state", $"property '{name}' references no materialized package file ({string.Join(", ", referenced.Take(3))})", 3);
            if (referenced.Count == 1 && present != 1)
                return Fail("resolve-state", $"reference '{referenced[0]}' is not a materialized package file", 3);
        }
    }
}
catch (Exception e)
{
    return Fail("resolve-state", $"state is not well-formed XML: {e.Message}", 3);
}

status["proxy_count"] = proxyCount;
status["stage"] = "render";

var failOutput = -1;
var failMatch = Regex.Match(stateText, @"FAKE-FAIL-OUTPUT-(\d+)");
if (failMatch.Success)
    failOutput = int.Parse(failMatch.Groups[1].Value);

var renderWidth = stateText.Contains("FAKE-WRONG-SIZE", StringComparison.Ordinal) ? width + 1 : width;
var totalSeconds = 0.0;
foreach (var output in outputs)
{
    var index = Int(output, "index");
    var outputPath = Str(output, "output_path");
    var timestepIndex = Int(output, "timestep_index");

    if (index == failOutput)
    {
        outputStatuses.Add(new Dictionary<string, object?> { ["index"] = index, ["ok"] = false, ["stage"] = "render", ["error"] = $"fake failure requested for output {index}", ["render_seconds"] = 0.0 });
        return Fail("render", $"output {index + 1} of {outputs.Count} (timestep {timestepIndex}): fake failure requested for output {index}", 1);
    }

    try
    {
        var started = DateTime.UtcNow;
        if (format == "jpeg")
            FakeImageWriter.WriteJpegHeaderOnly(outputPath, renderWidth, height);
        else
            FakeImageWriter.WritePng(outputPath, renderWidth, height, transparent, (byte)((timestepIndex * 37 + index * 11) % 256));

        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        totalSeconds += seconds;
        status["render_seconds"] = totalSeconds;
        outputStatuses.Add(new Dictionary<string, object?> { ["index"] = index, ["ok"] = true, ["stage"] = "done", ["error"] = "", ["render_seconds"] = seconds });
        Console.WriteLine($"render_task: output {index + 1}/{outputs.Count} (timestep {timestepIndex}) done in {seconds:F2} s");
    }
    catch (Exception e)
    {
        outputStatuses.Add(new Dictionary<string, object?> { ["index"] = index, ["ok"] = false, ["stage"] = "render", ["error"] = e.Message, ["render_seconds"] = 0.0 });
        return Fail("render", $"cannot write the output: {e.Message}", 1);
    }
}

if (stateText.Contains("FAKE-EXTRA-OUTPUT", StringComparison.Ordinal))
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Str(outputs[0], "output_path"))!, "stray.txt"), "stray");

status["stage"] = "done";
status["ok"] = true;
status["width"] = width;
status["height"] = height;

if (stateText.Contains("FAKE-NO-STATUS", StringComparison.Ordinal))
    return 0;

WriteStatus();
return 0;
