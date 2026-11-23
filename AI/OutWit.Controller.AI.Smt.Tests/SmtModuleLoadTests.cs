using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using OutWit.Common.Plugins;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Tests;

/// <summary>
/// The self-containment proof of the deployed module: the staged smt.module, laid out as a node
/// receives it (the controller, its model package, the Wasmtime.Dotnet binding, the native library
/// under runtimes/&lt;rid&gt;/native/, the solver module under wasm/), is loaded through the engine's
/// real plugin loader in an isolated load context, and from inside that context the sandbox
/// compiles the solver module and solves a problem. The tests that run packages through the engine
/// load the controller into the default context, where wasmtime resolves from the test bin, so they
/// cannot prove this.
/// <para>
/// The loader shares every assembly the default context already holds, as a node's host shares the
/// engine with its modules. A node never has the controller in its default context, and this test
/// process must not either: no type of the tests names a controller or a wasmtime type in its shape
/// (<c>SmtTestShapeTests</c>), this fixture names them as strings only, and it runs first
/// (<see cref="OrderAttribute"/>). On the build server a controller already loaded fails it; on a
/// developer's machine it is ignored, and run alone it gives the verdict:
///   dotnet test --filter FullyQualifiedName~SmtModuleLoadTests
/// The native library and the solver module are the packaged ones: the data assets the module's
/// manifest declares for this platform and for every platform, fetched from their releases and
/// held to the manifest (kept between runs under the tests' <c>@Data/.assets</c>), as a node is
/// sent them.
/// </para>
/// </summary>
[TestFixture]
[Order(0)]
[Category("Solver")]
public sealed class SmtModuleLoadTests
{
    #region Constants

    private const string SMT_ASSEMBLY = "OutWit.Controller.AI.Smt";
    private const string WASMTIME_ASSEMBLY = "Wasmtime.Dotnet";
    private const string SELF_CHECK_TYPE = "OutWit.Controller.AI.Smt.Sandbox.SmtSelfCheck";
    private const string SELF_CHECK_METHOD = "Run";
    private const string NATIVES_FOLDER = "runtimes/";
    private const string SOLVER_FILE = "z3-5.4.1.wasm";
    private const string SOLVER_ASSET = "smt-z3-wasm";
    private const string ANY_PLATFORM = "any";

    private const string NATIVE_LOADED = "wasmtime-native: loaded";
    private const string MANAGED_PREFIX = "wasmtime-managed: ";
    private const string NATIVE_PATH_PREFIX = "wasmtime-native-path: ";
    private const string MODULE_PREFIX = "solver-module: ";
    private const string PROBE = "probe: ok sat fuel=";

    #endregion

    #region Fields

    private string m_controllersPath = null!;
    private string m_moduleDir = null!;
    private string? m_savedModule;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is SMT_ASSEMBLY or WASMTIME_ASSEMBLY)
            .ToList();
        if (alreadyLoaded.Count > 0)
        {
            var reason = $"{string.Join(", ", alreadyLoaded)} already in the default load context (an earlier fixture, or a type of the tests that names a controller type); "
                         + "run this fixture alone for the isolation proof.";

            // On the build server the whole assembly runs in one process and this fixture is the
            // first: a controller already loaded there means the proof silently stopped running.
            if (SmtSolverModule.OnBuildServer)
                Assert.Fail(reason);

            Assert.Ignore(reason);
        }

        var root = SmtRigModules.SolutionRoot();
        if (root == null)
            SmtRigModules.Missing("the solution root was not found");

        m_controllersPath = SmtRigModules.Stage("module-load", SmtRigModules.CONTROLLER);
        m_moduleDir = Path.Combine(m_controllersPath, SmtRigModules.SMT);

        Assert.That(File.Exists(Path.Combine(m_moduleDir, $"{WASMTIME_ASSEMBLY}.dll")), Is.True,
            $"{WASMTIME_ASSEMBLY}.dll must be staged into smt.module by the controller's build");

        // The native library and the solver module are the packaged ones: the assets the module's
        // own manifest declares for this platform and for every platform, fetched from the
        // releases they name and held to their sizes and SHA-256.
        var rid = RuntimeInformation.RuntimeIdentifier;
        var declared = SmtPackagedAssets.Declared(m_moduleDir);
        var natives = declared.Where(asset => asset.RuntimeIdentifier == rid && asset.ExtractTo.StartsWith(NATIVES_FOLDER, StringComparison.Ordinal)).ToList();
        var everywhere = declared.Where(asset => asset.RuntimeIdentifier == ANY_PLATFORM).ToList();
        Assert.That(natives, Is.Not.Empty, $"smt.module's manifest declares no native engine library for '{rid}': a package made from it ships without one");
        Assert.That(everywhere.Select(asset => asset.Id), Does.Contain(SOLVER_ASSET), "smt.module's manifest declares the solver module: a package made from it ships without one");

        foreach (var asset in natives.Concat(everywhere))
        {
            try
            {
                SmtPackagedAssets.Resolve(asset, m_moduleDir, SmtPackagedAssets.CacheFolder(root!));
            }
            catch (HttpRequestException exception)
            {
                SmtRigModules.Missing($"the packaged asset '{asset.Id}' could not be fetched from {SmtPackagedAssets.Url(asset)}: {exception.Message}");
            }
        }

        Assert.That(File.Exists(Path.Combine(m_moduleDir, "wasm", SOLVER_FILE)), Is.True, "the solver module's asset puts it where the controller looks for it");

        // The module must be found beside the controller, not through the variable the tests set.
        m_savedModule = Environment.GetEnvironmentVariable(SmtTestEnvironment.MODULE_VARIABLE);
        Environment.SetEnvironmentVariable(SmtTestEnvironment.MODULE_VARIABLE, null);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_controllersPath == null)
            return;

        Environment.SetEnvironmentVariable(SmtTestEnvironment.MODULE_VARIABLE, m_savedModule);

        // The folder is the test's own; the native library may stay mapped into this process.
        SmtRigModules.TryDelete(m_controllersPath);
    }

    #endregion

    #region Module Load Tests

    [Test]
    public void TheStagedModuleSolvesFromAnIsolatedContextTest()
    {
        using var loader = new WitPluginLoader<IWitControllerNode>(m_controllersPath, useIsolatedContexts: true);
        loader.Load();

        var plugin = loader.Plugins.FirstOrDefault(candidate => candidate.GetType().Assembly.GetName().Name == SMT_ASSEMBLY);
        Assert.That(plugin, Is.Not.Null, "smt.module did not load through the plugin loader");

        var assembly = plugin!.GetType().Assembly;
        Assert.That(assembly.Location, Does.StartWith(m_moduleDir).IgnoreCase, "the controller comes from the staged module, not from the test bin");

        var run = assembly.GetType(SELF_CHECK_TYPE)?.GetMethod(SELF_CHECK_METHOD, BindingFlags.Public | BindingFlags.Static);
        Assert.That(run, Is.Not.Null, $"{SELF_CHECK_TYPE}.{SELF_CHECK_METHOD} not found in the loaded module");

        var report = (string)run!.Invoke(null, null)!;
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain(NATIVE_LOADED), report);
            Assert.That(report, Does.Contain(PROBE), "the solver ran and answered");
            Assert.That(ReportValue(report, MANAGED_PREFIX), Does.StartWith(m_moduleDir).IgnoreCase, "Wasmtime.Dotnet resolves from the module");
            Assert.That(ReportValue(report, NATIVE_PATH_PREFIX), Does.StartWith(m_moduleDir).IgnoreCase, "the module-aware resolver points at the module's runtimes/<rid>/native");
            Assert.That(ReportValue(report, MODULE_PREFIX), Does.StartWith(m_moduleDir).IgnoreCase, "the solver module is the one beside the controller");

            // macOS reports dyld images by their canonical path, which need not share the module
            // folder's prefix; the resolver's own report is the evidence there.
            if (!OperatingSystem.IsMacOS())
            {
                Assert.That(Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(module => module.FileName).Where(file => file.StartsWith(m_moduleDir, StringComparison.OrdinalIgnoreCase)),
                    Is.Not.Empty, "the native library is the module's copy, not the test bin's");
            }
        });
    }

    #endregion

    #region Tools

    private static string ReportValue(string report, string prefix)
    {
        var line = report.Split('\n').Select(one => one.TrimEnd('\r')).FirstOrDefault(one => one.StartsWith(prefix, StringComparison.Ordinal));
        return line == null ? "" : line[prefix.Length..];
    }

    #endregion
}
