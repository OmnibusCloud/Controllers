using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

// ConsumerRunner — renders corpus scenes through the ParaView controller the way a deployment does:
// host + worker-node engines loading the modules of a CONSUMER's @Controllers folder (no controller
// assembly referenced by this process, so Assembly.Location of every controller type is the module
// folder — the path the runtime resolver reads), the bundled scripts, a file blob store.
//
//   ConsumerRunner --controllers <dir> --scripts <dir> --corpus <dir> --out <dir> [--state <name>]...
//
// Exit code 0 only when every requested scene rendered a valid PNG (and the PVD series all its frames).

var arguments = ParseArguments(args);
var controllers = Required(arguments, "controllers");
var scripts = Required(arguments, "scripts");
var corpus = Required(arguments, "corpus");
var output = Required(arguments, "out");
var states = arguments.TryGetValue("state", out var requested) && requested.Count > 0
    ? requested
    : ["sphere_static.pvsm", "vti_contour.pvsm", "gui/gui_filters.pvsm", "OmnibusCloudFrdReader/frd_static.pvsm", "pvd_series.pvsm"];
Directory.CreateDirectory(output);

var blobs = new FileBlobService(Path.Combine(output, "blobs"));
WitEngineNodeSdk.Instance.Reload(
    useIsolatedContext: false,
    moduleFolder: controllers,
    configureServices: services => services.AddSingleton<IWitBlobService>(blobs));

var engine = WitEngineSdk.Instance;
engine.Reload(
    useIsolatedContext: false,
    logger: null,
    moduleFolder: controllers,
    configureServices: services =>
    {
        services.AddSingleton<IWitBlobService>(blobs);
        services.AddSingleton<IWitNodesManager>(new SingleNodeManager(WitEngineNodeSdk.Instance));
    });

var model = FindAssembly("OutWit.Controller.Visualization.ParaView.Model");
var controller = FindAssembly("OutWit.Controller.Visualization.ParaView");
Console.WriteLine($"controller assembly: {controller.Location}");
Console.WriteLine($"model assembly     : {model.Location}");
if (!controller.Location.Replace('\\', '/').Contains("/paraview.module/", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("FAIL: the controller was not loaded from the consumer's paraview.module");
    return 2;
}

var runtimeInfo = controller.GetType("OutWit.Controller.Visualization.ParaView.Runtime.ParaViewRuntimeInfo")!;
var major = (int)runtimeInfo.GetField("RUNTIME_MAJOR")!.GetValue(null)!;
var minor = (int)runtimeInfo.GetField("RUNTIME_MINOR")!.GetValue(null)!;
var patch = (int)runtimeInfo.GetField("RUNTIME_PATCH")!.GetValue(null)!;
var pluginName = (string)runtimeInfo.GetField("FRD_READER_PLUGIN_NAME")!.GetValue(null)!;
var readerVersion = (string?)runtimeInfo.GetMethod("BundledReaderVersion")!.Invoke(null, null);
Console.WriteLine($"runtime {major}.{minor}.{patch}, bundled reader {readerVersion ?? "none"}");

var failures = 0;
foreach (var stateName in states)
{
    try
    {
        var statePath = Path.Combine(corpus, "states", stateName.Replace('/', Path.DirectorySeparatorChar));
        var stateXml = File.ReadAllText(statePath);
        var files = ReferencedFiles(stateXml, corpus);
        var needsReader = stateName.StartsWith("OmnibusCloudFrdReader/", StringComparison.Ordinal);
        var scene = BuildScene(model, blobs, corpus, statePath, files, major, minor, patch, needsReader ? pluginName : null, readerVersion);
        var isSeries = stateName.Contains("pvd_series", StringComparison.Ordinal);

        var options = Activator.CreateInstance(model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewOutputOptionsData")!)!;
        SetProperty(options, "Width", 320);
        SetProperty(options, "Height", 240);
        if (isSeries)
        {
            var frames = Activator.CreateInstance(model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewFrameSelectionData")!)!;
            var modeType = model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewFrameSelectionMode")!;
            SetProperty(frames, "Mode", Enum.Parse(modeType, "All"));
            SetProperty(options, "Frames", frames);
        }

        var script = File.ReadAllText(Path.Combine(scripts, isSeries ? "RenderParaViewFrames.wit" : "RenderParaViewStill.wit"));
        var job = engine.Compile(script);
        var started = DateTime.UtcNow;
        var status = await engine.ScheduleAndWaitAsync(job, scene, options);
        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        if (status.Result != WitProcessingResult.Completed)
        {
            Console.WriteLine($"FAIL {stateName}: {status.Result}: {status.Message}");
            failures++;
            continue;
        }

        var resultValue = job.Variables["result"].Value;
        var blobIds = resultValue switch
        {
            Guid single => new List<Guid> { single },
            IEnumerable<Guid?> many => many.Where(me => me.HasValue).Select(me => me!.Value).ToList(),
            IEnumerable<Guid> manyPlain => manyPlain.ToList(),
            _ => []
        };
        var pngs = 0;
        foreach (var blobId in blobIds)
        {
            var path = blobs.PathOf(blobId);
            var header = new byte[8];
            using (var stream = File.OpenRead(path))
                _ = stream.Read(header, 0, 8);
            var isPng = header.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            if (isPng)
            {
                pngs++;
                File.Copy(path, Path.Combine(output, $"{stateName.Replace('/', '_')}_{pngs}.png"), overwrite: true);
            }
        }
        var expected = isSeries ? 5 : 1;
        if (pngs != expected)
        {
            Console.WriteLine($"FAIL {stateName}: expected {expected} PNG(s), got {pngs} of {blobIds.Count} result blob(s)");
            failures++;
            continue;
        }
        Console.WriteLine($"OK   {stateName}: {pngs} PNG(s) in {elapsed:F1}s");
    }
    catch (Exception error)
    {
        Console.WriteLine($"FAIL {stateName}: {error.GetType().Name}: {error.Message}");
        failures++;
    }
}

Console.WriteLine(failures == 0 ? "consumer runner: all scenes rendered" : $"consumer runner: {failures} failure(s)");
return failures == 0 ? 0 : 1;

// ----------------------------------------------------------------------------------------------------

static Dictionary<string, List<string>> ParseArguments(string[] args)
{
    var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        var key = args[i].TrimStart('-');
        if (!result.TryGetValue(key, out var list))
            result[key] = list = [];
        list.Add(args[i + 1]);
    }
    return result;
}

static string Required(Dictionary<string, List<string>> arguments, string key)
{
    return arguments.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : throw new ArgumentException($"--{key} is required");
}

static Assembly FindAssembly(string name)
{
    return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(me => string.Equals(me.GetName().Name, name, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"assembly {name} is not loaded (module missing?)");
}

static IReadOnlyList<string> ReferencedFiles(string stateXml, string corpus)
{
    // Logical file paths the state references: FileName / FileNames properties with the files domain —
    // plus, for a PVD index, the pieces it lists (the producing plugin attaches them; the state does not
    // name them).
    var document = XDocument.Parse(stateXml);
    var files = new List<string>();
    foreach (var property in document.Descendants("Property"))
    {
        var name = (string?)property.Attribute("name");
        if (name != "FileName" && name != "FileNames")
            continue;
        if (!property.Elements("Domain").Any(me => (string?)me.Attribute("name") == "files"))
            continue;
        foreach (var element in property.Elements("Element"))
        {
            var value = (string?)element.Attribute("value");
            if (!string.IsNullOrEmpty(value) && !files.Contains(value))
                files.Add(value);
        }
    }
    foreach (var index in files.Where(me => me.EndsWith(".pvd", StringComparison.OrdinalIgnoreCase)).ToList())
    {
        var indexPath = Path.Combine(corpus, index.Replace('/', Path.DirectorySeparatorChar));
        var folder = index.Contains('/') ? index[..(index.LastIndexOf('/') + 1)] : string.Empty;
        foreach (var dataSet in XDocument.Load(indexPath).Descendants("DataSet"))
        {
            var piece = (string?)dataSet.Attribute("file");
            if (!string.IsNullOrEmpty(piece) && !files.Contains(folder + piece))
                files.Add(folder + piece);
        }
    }
    return files;
}

static object BuildScene(Assembly model, FileBlobService blobs, string corpus, string statePath, IReadOnlyList<string> files, int major, int minor, int patch, string? pluginName, string? readerVersion)
{
    var sceneType = model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewSceneRefData")!;
    var attachmentType = model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewAttachmentRefData")!;
    var runtimeType = model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewRuntimeRequirementData")!;
    var pluginType = model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewPluginRequirementData")!;
    var roleType = model.GetType("OutWit.Controller.Visualization.ParaView.Model.ParaViewAttachmentRole")!;

    var scene = Activator.CreateInstance(sceneType)!;
    var stateBlob = blobs.Register(statePath);
    SetProperty(scene, "StateBlobId", stateBlob);
    SetProperty(scene, "StateSha256", Sha256Of(statePath));
    SetProperty(scene, "StateSize", new FileInfo(statePath).Length);

    var attachments = (System.Collections.IList)sceneType.GetProperty("Attachments")!.GetValue(scene)!;
    var seriesPieces = files.Where(me => me.Contains("/series_", StringComparison.Ordinal)).OrderBy(me => me, StringComparer.Ordinal).ToList();
    foreach (var logicalPath in files)
    {
        var localPath = Path.Combine(corpus, logicalPath.Replace('/', Path.DirectorySeparatorChar));
        var attachment = Activator.CreateInstance(attachmentType)!;
        SetProperty(attachment, "BlobId", blobs.Register(localPath));
        SetProperty(attachment, "LogicalPath", logicalPath);
        SetProperty(attachment, "Sha256", Sha256Of(localPath));
        SetProperty(attachment, "Size", new FileInfo(localPath).Length);
        var isIndex = logicalPath.EndsWith(".pvd", StringComparison.OrdinalIgnoreCase);
        var isPiece = seriesPieces.Contains(logicalPath);
        SetProperty(attachment, "Role", Enum.Parse(roleType, isIndex ? "SeriesIndex" : "ReaderInput"));
        if (isIndex || isPiece)
            SetProperty(attachment, "SeriesGroup", "series");
        if (isPiece)
        {
            var ordinal = seriesPieces.IndexOf(logicalPath);
            SetProperty(attachment, "SeriesOrdinal", ordinal);
            ((System.Collections.IList)attachmentType.GetProperty("TimestepIndices")!.GetValue(attachment)!).Add(ordinal);
        }
        attachments.Add(attachment);
    }

    var runtime = Activator.CreateInstance(runtimeType)!;
    SetProperty(runtime, "ParaViewMajor", major);
    SetProperty(runtime, "ParaViewMinor", minor);
    SetProperty(runtime, "ParaViewPatch", patch);
    SetProperty(runtime, "ProducerPluginVersion", "0.0.0-consumer");
    SetProperty(runtime, "ProducerPlatform", "consumer-runner");
    if (pluginName != null)
    {
        var plugin = Activator.CreateInstance(pluginType)!;
        SetProperty(plugin, "Name", pluginName);
        SetProperty(plugin, "Version", readerVersion ?? "1.0.0");
        ((System.Collections.IList)runtimeType.GetProperty("Plugins")!.GetValue(runtime)!).Add(plugin);
    }
    SetProperty(scene, "Runtime", runtime);
    return scene;
}

static void SetProperty(object target, string name, object? value)
{
    target.GetType().GetProperty(name)!.SetValue(target, value);
}

static string Sha256Of(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

// ----------------------------------------------------------------------------------------------------

internal sealed class FileBlobService : IWitBlobService
{
    private readonly ConcurrentDictionary<Guid, string> m_paths = new();
    private readonly string m_storage;

    public FileBlobService(string storage)
    {
        m_storage = storage;
        Directory.CreateDirectory(storage);
    }

    public Guid Register(string path)
    {
        var id = Guid.NewGuid();
        m_paths[id] = path;
        return id;
    }

    public string PathOf(Guid id) => m_paths[id];

    public Task<string> GetLocalPathAsync(Guid blobId)
    {
        return m_paths.TryGetValue(blobId, out var path) ? Task.FromResult(path) : throw new FileNotFoundException($"blob {blobId} unknown");
    }

    public Task<Guid> UploadFileAsync(string localFilePath)
    {
        var id = Guid.NewGuid();
        var destination = Path.Combine(m_storage, $"{id:N}{Path.GetExtension(localFilePath)}");
        File.Copy(localFilePath, destination, overwrite: true);
        m_paths[id] = destination;
        return Task.FromResult(id);
    }

    public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
    {
        var id = Guid.NewGuid();
        var destination = Path.Combine(m_storage, $"{id:N}{Path.GetExtension(fileName)}");
        File.WriteAllBytes(destination, data);
        m_paths[id] = destination;
        return Task.FromResult(id);
    }
}

internal sealed class SingleNodeManager : IWitNodesManager
{
    private readonly IWitEngineNode m_node;

    public SingleNodeManager(IWitEngineNode node)
    {
        m_node = node;
        CompatibleNodes = [new SingleActivityNode(node)];
    }

    public IReadOnlyList<IWitEngineActivityNode> CompatibleNodes { get; }

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes<TActivity>(IWitProcessingOptions options) where TActivity : IWitActivity
        => Task.FromResult(CompatibleNodes);

    public Task<IReadOnlyList<IWitEngineActivityNode>> GetCompatibleNodes(Type activityType, IWitProcessingOptions options)
        => Task.FromResult(CompatibleNodes);

    public Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(Guid nodeId, Guid jobId, IWitActivity activity, IWitVariablesCollection pool, IReadOnlyList<string> returnVariables)
        => m_node.Process(jobId, activity, pool, returnVariables);

    public async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessBatch(Guid nodeId, Guid jobId, IReadOnlyList<WitNodeTaskRequest> requests, bool canRunInParallelOnClient)
    {
        var all = new List<IWitVariable>();
        IWitProcessingStatus? last = null;
        foreach (var request in requests)
        {
            var (status, variables) = await m_node.Process(jobId, request.Activity, request.Pool, request.ReturnVariables);
            last = status;
            all.AddRange(variables);
            if (status.Result == WitProcessingResult.Failed)
                return (status, all);
        }
        return (last ?? throw new InvalidOperationException("no requests"), all);
    }
}

internal sealed class SingleActivityNode : IWitEngineActivityNode
{
    public SingleActivityNode(IWitEngineNodeBase node)
    {
        NodeId = node.Id;
    }

    public Guid NodeId { get; }

    public IWitBenchmarkResult BenchmarkResult => WitBenchmarkResult.Default;
}
