using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// The distributed test rig for the bundled scripts: the engine SDK host with the modules the rig
/// names (<see cref="CorpusRigModules"/>: the controller under test and what its scripts call,
/// each checked to be this build's, and nothing else that happens to be staged), in-process mock
/// nodes, a file-backed blob service, and a loopback origin that serves the golden shards — the
/// "nodes" really fetch over HTTP and run the pipeline.
/// </summary>
public abstract class CorpusScriptTestBase
{
    #region Fields

    private string m_blobStoragePath = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void ScriptBaseSetUp()
    {
        SolutionRoot = FindSolutionRoot() ?? throw new InvalidOperationException("The rig cannot find the solution root (OutWit.slnx) above the test folder.");

        // A named set, each module held to this build: a missing, mixed or foreign module fails the
        // fixture by name instead of being skipped or loaded in silence.
        var modules = CorpusRigModules.Folder;

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witai_corpus_e2e_{Guid.NewGuid():N}");
        BlobService = new CorpusTestBlobService(Path.Combine(m_blobStoragePath, "blobs"));
        Origin = new CorpusTestOrigin(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures"));

        var tempStorage = new WitTempStorageDefault(Path.Combine(m_blobStoragePath, "temp"));
        NodesManager = new CorpusTestNodesManager(WitEngineNodeSdk.Instance, BlobService, Nodes);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: modules,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(BlobService);
                services.AddSingleton<IWitTempStorage>(tempStorage);
            });

        Engine = WitEngineSdk.Instance;
        Engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: modules,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(BlobService);
                services.AddSingleton<IWitTempStorage>(tempStorage);
                services.AddSingleton<IWitNodesManager>(NodesManager);
            });
    }

    [OneTimeTearDown]
    public void ScriptBaseTearDown()
    {
        Origin?.Dispose();

        // The adapters hand each task's scope over for deletion; it lives under this folder.
        CorpusTestFolders.Delete(m_blobStoragePath);
    }

    #endregion

    #region Tools

    /// <summary>Reads a bundled script by file name; a script that is missing fails the fixture.</summary>
    protected string LoadScript(string fileName)
    {
        return File.ReadAllText(Path.Combine(SolutionRoot, "AI", "OutWit.Controller.AI.Corpus", "Scripts", fileName));
    }

    protected async Task<Guid> UploadAsync(string text, string name)
    {
        return await BlobService.UploadBytesAsync(Encoding.UTF8.GetBytes(text), name);
    }

    protected async Task<Guid> UploadFixtureAsync(string name)
    {
        return await BlobService.UploadFileAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name));
    }

    protected async Task<byte[]> ReadBytesAsync(Guid blobId)
    {
        return await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync(blobId));
    }

    protected async Task<JsonDocument> ReadJsonAsync(Guid blobId)
    {
        return JsonDocument.Parse(await ReadBytesAsync(blobId));
    }

    /// <summary>The tasks behind a job variable that holds task packages: the blobs are opened and read.</summary>
    protected List<CorpusTaskData> TasksOf(object? packages)
    {
        return CorpusTestPackages.Tasks(BlobService, packages);
    }

    /// <summary>The results behind a job variable that holds answers: the blobs are opened and read.</summary>
    protected List<CorpusResultData> ResultsOf(object? answers)
    {
        return CorpusTestPackages.Results(BlobService, answers);
    }

    /// <summary>What a job's status says, all of it: the result and the message of the job and of every step under it (a status's own ToString has no message).</summary>
    protected static string Describe(IWitProcessingStatus status)
    {
        var text = new StringBuilder();
        var pending = new Stack<IWitProcessingStatus>();
        pending.Push(status);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            text.Append(current.Result).Append(": ").AppendLine(current.Message);
            foreach (var child in current.Children)
                pending.Push(child);
        }

        return text.ToString();
    }

    /// <summary>A manifest over golden shards served by the loopback origin.</summary>
    protected string Manifest(params string[] fixtures)
    {
        var sources = string.Join(", ", fixtures.Select(fixture => $"\"{Origin.ShardUrl(fixture)}\""));
        return $$"""{"pipeline":{"version":"e2e-v1"},"allowed_origins":["{{Origin.Host}}"],"sources":[{{sources}}]}""";
    }

    private static string? FindSolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    #endregion

    #region Properties

    /// <summary>How many mock nodes the rig has (three unless a fixture says otherwise).</summary>
    protected virtual int Nodes => 3;

    protected string SolutionRoot { get; private set; } = null!;

    protected IWitEngine Engine { get; private set; } = null!;

    protected CorpusTestBlobService BlobService { get; private set; } = null!;

    protected CorpusTestOrigin Origin { get; private set; } = null!;

    /// <summary>The rig's nodes; a test may make one of them dishonest.</summary>
    internal CorpusTestNodesManager NodesManager { get; private set; } = null!;

    #endregion
}
