using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// The distributed gate: the bundled VerifyRun.wit script running through the engine
/// (Verify.Split → Grid.ForEach ⇒ Verify.ExecuteBatch → Verify.Collect, blob transport,
/// mock in-process nodes) executes a real taskset in the sandbox and returns a verdict
/// report — the RLVR code-vs-tests pipeline end to end. Opt-in on the runtime download,
/// like the sandbox tests: it actually runs Python/JS in wasmtime on the "nodes".
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyRunSkeletonTests
{
    #region Fields

    private string m_blobStoragePath = null!;
    private VerifyTestBlobService m_blobService = null!;
    private string m_verifyRunScript = null!;
    private IWitEngine m_engine = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var runtimesRoot = Path.Combine(
            solutionRoot, "AI", "OutWit.Controller.AI.Verify.Tests", "@Data", "runtimes");
        if (VerifyRuntimeCatalog.Resolve(runtimesRoot, VerifyRuntimeCatalog.PYTHON_3_14_6, out _) == null)
            Assert.Ignore($"Pinned runtimes not resolvable under '{runtimesRoot}' — run download-spike-runtimes.ps1 to opt in.");

        // The in-process nodes read this to locate the runtime archive.
        Environment.SetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar, runtimesRoot);

        var scriptPath = Path.Combine(
            solutionRoot, "AI", "OutWit.Controller.AI.Verify", "Scripts", "VerifyRun.wit");
        if (!File.Exists(scriptPath))
            Assert.Ignore($"VerifyRun.wit not found at {scriptPath}");
        m_verifyRunScript = File.ReadAllText(scriptPath);

        var controllersPath = FindControllersPath();
        if (controllersPath == null)
            Assert.Ignore("@Controllers not found");

        m_blobStoragePath = Path.Combine(Path.GetTempPath(), $"witai_verify_e2e_{Guid.NewGuid():N}");
        m_blobService = new VerifyTestBlobService(m_blobStoragePath);

        WitEngineNodeSdk.Instance.Reload(
            useIsolatedContext: false,
            moduleFolder: controllersPath,
            configureServices: services => services.AddSingleton<IWitBlobService>(m_blobService));

        m_engine = WitEngineSdk.Instance;
        m_engine.Reload(
            useIsolatedContext: false,
            logger: null,
            moduleFolder: controllersPath,
            configureServices: services =>
            {
                services.AddSingleton<IWitBlobService>(m_blobService);
                services.AddSingleton<IWitNodesManager>(new VerifyTestNodesManager(WitEngineNodeSdk.Instance));
            });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(VerifyRuntimeLocator.OverrideEnvVar, null);
        if (Directory.Exists(m_blobStoragePath))
            Directory.Delete(m_blobStoragePath, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void BundledScriptCompilesTest()
    {
        var job = m_engine.Compile(m_verifyRunScript);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "taskset", "opts", "batches", "results", "report" }));
    }

    [Test]
    public async Task DistributedRunProducesVerdictReportTest()
    {
        // A taskset mixing a passing suite, a failing suite, a runtime error, and a JS task —
        // the whole thing fans out to the mock nodes and comes back as one verdict report.
        var taskset = new StringBuilder();
        taskset.AppendLine("""{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"a=int(input());b=int(input());print(a+b)"},"suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}]}""");
        taskset.AppendLine("""{"index":1,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"print('wrong')"},"suite":[{"expected_stdout":"right\n","expected_exit":0}]}""");
        taskset.AppendLine("""{"index":2,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"raise SystemExit(4)"}}""");
        taskset.AppendLine("""{"index":3,"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}""");

        var tasksetBlobId = await m_blobService.UploadBytesAsync(
            Encoding.UTF8.GetBytes(taskset.ToString()), "taskset.jsonl");

        var job = m_engine.Compile(m_verifyRunScript);
        var status = await m_engine.ScheduleAndWaitAsync(job, tasksetBlobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var reportBlobId = (Guid)job.Variables["report"].Value!;
        var reportPath = await m_blobService.GetLocalPathAsync(reportBlobId);
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(reportPath));

        var summary = report.RootElement.GetProperty("summary");
        Assert.That(summary.GetProperty("total").GetInt32(), Is.EqualTo(4));
        Assert.That(summary.GetProperty("pass").GetInt32(), Is.EqualTo(2), "task 0 (suite) and task 3 (js) pass");

        var verdictByIndex = report.RootElement.GetProperty("results").EnumerateArray()
            .ToDictionary(r => r.GetProperty("index").GetInt32(), r => r.GetProperty("verdict").GetString());
        Assert.That(verdictByIndex[0], Is.EqualTo("Pass"));
        Assert.That(verdictByIndex[1], Is.EqualTo("Fail"));
        Assert.That(verdictByIndex[2], Is.EqualTo("RuntimeError"));
        Assert.That(verdictByIndex[3], Is.EqualTo("Pass"));
    }

    [Test]
    public async Task DeterministicRewardsAreReproducibleTest()
    {
        // The RLVR artifact: the same taskset yields the same verdicts on every run — what a
        // reward pipeline depends on.
        const string tasksetJson =
            """{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"import math;print(sum(math.isqrt(i) for i in range(10000)))"},"suite":[{"expected_stdout":"661650\n","expected_exit":0}]}""";

        async Task<string> RunOnce()
        {
            var blobId = await m_blobService.UploadBytesAsync(Encoding.UTF8.GetBytes(tasksetJson + "\n"), "taskset.jsonl");
            var job = m_engine.Compile(m_verifyRunScript);
            var status = await m_engine.ScheduleAndWaitAsync(job, blobId, new VerifyOptionsData());
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            var reportPath = await m_blobService.GetLocalPathAsync((Guid)job.Variables["report"].Value!);
            return await File.ReadAllTextAsync(reportPath);
        }

        var first = await RunOnce();
        var second = await RunOnce();

        Assert.That(second, Is.EqualTo(first), "verdict report must be byte-identical across runs");
        Assert.That(first, Does.Contain("\"pass\":1"));
    }

    #endregion

    #region Helpers

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
}
