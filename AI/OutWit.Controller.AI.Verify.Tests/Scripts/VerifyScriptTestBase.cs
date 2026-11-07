using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// The distributed test rig for the bundled scripts: the engine SDK host with the staged
/// controllers, in-process mock nodes and a file-backed blob service. Opt-in on the runtime
/// download, like the sandbox tests: the "nodes" really run Python/JS in wasmtime.
/// </summary>
public abstract class VerifyScriptTestBase
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

        var runtimesRoot = Path.Combine(solutionRoot, "AI", "OutWit.Controller.AI.Verify.Tests", "@Data", "runtimes");
        if (VerifyRuntimeCatalog.Resolve(runtimesRoot, VerifyRuntimeCatalog.PYTHON_3_14_6, out _) == null)
            Assert.Ignore($"Pinned runtimes not resolvable under '{runtimesRoot}' — run verify-assets build @Data/runtimes to opt in.");

        // The in-process nodes read this to locate the runtime archive.
        Environment.SetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar, runtimesRoot);

        var controllersPath = FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witai_verify_e2e_{Guid.NewGuid():N}");
        BlobService = new VerifyTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(BlobService));

        Engine = WitEngineSdk.Instance;
        Engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(BlobService);
                services.AddSingleton<IWitNodesManager>(new VerifyTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [OneTimeTearDown]
    public void ScriptBaseTearDown()
    {
        Environment.SetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar, null);
        if (m_blobStoragePath != null && Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tools

    /// <summary>Reads a bundled script by file name, or ignores the fixture when it is missing.</summary>
    protected string LoadScript(string fileName)
    {
        var path = Path.Combine(SolutionRoot, "AI", "OutWit.Controller.AI.Verify", "Scripts", fileName);
        if (!File.Exists(path))
            Assert.Ignore($"{fileName} not found at {path}");

        return File.ReadAllText(path);
    }

    protected async Task<Guid> UploadAsync(string text, string name)
    {
        return await BlobService.UploadBytesAsync(Encoding.UTF8.GetBytes(text), name);
    }

    protected async Task<string> ReadTextAsync(Guid blobId)
    {
        return await File.ReadAllTextAsync(await BlobService.GetLocalPathAsync(blobId));
    }

    protected async Task<JsonDocument> ReadJsonAsync(Guid blobId)
    {
        return JsonDocument.Parse(await File.ReadAllBytesAsync(await BlobService.GetLocalPathAsync(blobId)));
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
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "@Controllers", "Debug");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    #endregion

    #region Properties

    protected string SolutionRoot { get; private set; } = null!;

    private protected VerifyTestBlobService BlobService { get; private set; } = null!;

    protected IWitEngine Engine { get; private set; } = null!;

    #endregion
}
