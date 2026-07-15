using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Shared plumbing for the sandbox behavioral tests: resolves the pinned runtime root
/// (Assert.Ignore when absent — the tests are opt-in on the runtime download) and owns
/// one <see cref="VerifyWasmSandbox"/> per fixture so runtime modules compile once.
/// </summary>
public abstract class VerifySandboxTestBase
{
    #region Fields

    protected string RuntimesRoot { get; private set; } = null!;
    protected VerifyResolvedRuntime Python { get; private set; } = null!;
    protected VerifyResolvedRuntime QuickJs { get; private set; } = null!;
    protected VerifyWasmSandbox Sandbox { get; private set; } = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void BaseOneTimeSetUp()
    {
        var projectRoot = FindTestProjectRoot();
        if (projectRoot == null)
            Assert.Ignore("Test project root not found");

        RuntimesRoot = Path.Combine(projectRoot, "@Data", "runtimes");

        var python = VerifyRuntimeCatalog.Resolve(RuntimesRoot, VerifyRuntimeCatalog.PYTHON_3_14_6, out var pyReason);
        var quickJs = VerifyRuntimeCatalog.Resolve(RuntimesRoot, VerifyRuntimeCatalog.QUICKJS_0_15_1, out var jsReason);
        if (python == null || quickJs == null)
        {
            Assert.Ignore(
                $"Pinned runtimes not resolvable under '{RuntimesRoot}' " +
                $"(python: {pyReason ?? "ok"}; quickjs: {jsReason ?? "ok"}) — " +
                "run download-spike-runtimes.ps1 to opt in.");
        }

        Python = python!;
        QuickJs = quickJs!;
        Sandbox = new VerifyWasmSandbox();
    }

    [OneTimeTearDown]
    public void BaseOneTimeTearDown()
    {
        Sandbox?.Dispose();
    }

    #endregion

    #region Helpers

    protected static VerifyTaskData PythonTask(string code, int index = 0, VerifySuiteData? suite = null,
        List<string>? args = null, string? stdin = null, VerifyLimitsData? limits = null)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6,
            Sources = [new VerifySourceFileData { Name = "main.py", Content = code }],
            EntryPoint = "main.py",
            Args = args ?? [],
            Stdin = stdin,
            Suite = suite,
            Limits = limits
        };
    }

    protected static VerifyTaskData JsTask(string code, int index = 0, VerifyLimitsData? limits = null)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.QUICKJS_0_15_1,
            Sources = [new VerifySourceFileData { Name = "main.js", Content = code }],
            EntryPoint = "main.js",
            Limits = limits
        };
    }

    protected VerifyResultData Run(VerifyResolvedRuntime runtime, VerifyTaskData task, VerifyLimitsData? limits = null)
    {
        return Sandbox.Execute(runtime, task, VerifySandboxDefaults.Resolve(limits ?? task.Limits, null));
    }

    private static string? FindTestProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.Controller.AI.Verify.Tests.csproj")))
            directory = directory.Parent;

        return directory?.FullName;
    }

    #endregion
}
