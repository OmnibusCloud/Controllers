using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Shared plumbing for the sandbox behavioral tests: resolves the pinned runtime root
/// (Assert.Ignore when absent — the tests are opt-in on the runtime download; on the build
/// server, where the set is always built, an unresolved runtime is a failure) and owns
/// one <see cref="VerifyWasmSandbox"/> per fixture so runtime modules compile once.
/// </summary>
public abstract class VerifySandboxTestBase
{
    #region Constants

    private const string CI_ENV = "GITHUB_ACTIONS";

    #endregion

    #region Fields

    protected string RuntimesRoot { get; private set; } = null!;
    protected VerifyResolvedRuntime Python { get; private set; } = null!;
    protected VerifyResolvedRuntime PythonSympy { get; private set; } = null!;
    protected VerifyResolvedRuntime QuickJs { get; private set; } = null!;
    protected VerifyResolvedRuntime Ruby { get; private set; } = null!;
    protected VerifyResolvedRuntime Lua { get; private set; } = null!;
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
        var sympy = VerifyRuntimeCatalog.Resolve(RuntimesRoot, VerifyRuntimeCatalog.PYTHON_3_14_6_SYMPY_1_14_0, out var sympyReason);
        var quickJs = VerifyRuntimeCatalog.Resolve(RuntimesRoot, VerifyRuntimeCatalog.QUICKJS_0_15_1, out var jsReason);
        var ruby = VerifyRuntimeCatalog.Resolve(RuntimesRoot, VerifyRuntimeCatalog.RUBY_3_4_1, out var rubyReason);
        var lua = VerifyRuntimeCatalog.Resolve(RuntimesRoot, VerifyRuntimeCatalog.LUA_5_4_8, out var luaReason);
        if (python == null || sympy == null || quickJs == null || ruby == null || lua == null)
        {
            var reason = $"Pinned runtimes not resolvable under '{RuntimesRoot}' " +
                         $"(python: {pyReason ?? "ok"}; sympy: {sympyReason ?? "ok"}; quickjs: {jsReason ?? "ok"}; ruby: {rubyReason ?? "ok"}; lua: {luaReason ?? "ok"}) — " +
                         "run verify-assets build @Data/runtimes to opt in.";

            // The build server builds the set from its pins before the tests: a runtime that
            // does not resolve there is a broken pin (a library tree that hashes differently
            // on this platform, say), and skipping would hide every sandbox test behind it.
            if (string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase))
                Assert.Fail(reason);

            Assert.Ignore(reason);
        }

        Python = python!;
        PythonSympy = sympy!;
        QuickJs = quickJs!;
        Ruby = ruby!;
        Lua = lua!;
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

    protected static VerifyTaskData RubyTask(string code, int index = 0, VerifySuiteData? suite = null,
        List<string>? args = null, string? stdin = null, VerifyLimitsData? limits = null)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.RUBY_3_4_1,
            Sources = [new VerifySourceFileData { Name = "main.rb", Content = code }],
            EntryPoint = "main.rb",
            Args = args ?? [],
            Stdin = stdin,
            Suite = suite,
            Limits = limits
        };
    }

    protected static VerifyTaskData LuaTask(string code, int index = 0, VerifySuiteData? suite = null,
        List<string>? args = null, string? stdin = null, VerifyLimitsData? limits = null, params VerifySourceFileData[] more)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = VerifyRuntimeCatalog.LUA_5_4_8,
            Sources = [new VerifySourceFileData { Name = "main.lua", Content = code }, .. more],
            EntryPoint = "main.lua",
            Args = args ?? [],
            Stdin = stdin,
            Suite = suite,
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
