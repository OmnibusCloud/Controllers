using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.AI.Corpus.Tests.Scripts;

/// <summary>
/// The distributed test rig for the bundled scripts: the engine SDK host with the staged
/// controllers, in-process mock nodes, a file-backed blob service, and a loopback origin that
/// serves the golden shards — the "nodes" really fetch over HTTP and run the pipeline.
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
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        SolutionRoot = solutionRoot;

        var controllersPath = FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witai_corpus_e2e_{Guid.NewGuid():N}");
        BlobService = new CorpusTestBlobService(Path.Combine(m_blobStoragePath, "blobs"));
        Origin = new CorpusTestOrigin(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures"));

        var tempStorage = new WitTempStorageDefault(Path.Combine(m_blobStoragePath, "temp"));
        NodesManager = new CorpusTestNodesManager(WitEngineNodeSdk.Instance);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(BlobService);
                services.AddSingleton<IWitTempStorage>(tempStorage);
            });

        Engine = WitEngineSdk.Instance;
        Engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
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
        if (m_blobStoragePath != null && Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    /// <summary>Reads a bundled script by file name, or ignores the fixture when it is missing.</summary>
    protected string LoadScript(string fileName)
    {
        var path = Path.Combine(SolutionRoot, "AI", "OutWit.Controller.AI.Corpus", "Scripts", fileName);
        if (!File.Exists(path))
            Assert.Ignore($"{fileName} not found at {path}");

        return File.ReadAllText(path);
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

    private static string? FindControllersPath()
    {
        // The staged modules live under @Controllers/<Configuration> at the solution root; the
        // configuration is the one this test assembly was built with (Release in CI).
        var configuration = typeof(CorpusScriptTestBase).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>()
            .FirstOrDefault()?.Configuration ?? "Debug";

        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", configuration);
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    #endregion

    #region Properties

    protected string SolutionRoot { get; private set; } = null!;

    protected IWitEngine Engine { get; private set; } = null!;

    protected CorpusTestBlobService BlobService { get; private set; } = null!;

    protected CorpusTestOrigin Origin { get; private set; } = null!;

    /// <summary>The rig's three nodes; a test may make one of them dishonest.</summary>
    internal CorpusTestNodesManager NodesManager { get; private set; } = null!;

    #endregion
}
