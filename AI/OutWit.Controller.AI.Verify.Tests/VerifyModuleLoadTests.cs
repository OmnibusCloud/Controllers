using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using OutWit.Common.Plugins;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests;

/// <summary>
/// The self-containment proof of the deployed module (requirement X-13): the staged
/// verify.module, laid out as a node receives it (the controller, its Model, the
/// Wasmtime.Dotnet binding, runtimes/&lt;rid&gt;/native/ and wasm/), is loaded through the
/// engine's real plugin loader in an isolated load context, and the sandbox runs one task
/// per language runtime from inside that context. The in-process e2e cannot prove this: it
/// loads the controllers into the default context, where wasmtime resolves from the test bin.
///
/// The loader shares every assembly the default context already holds, exactly as a node
/// host shares the engine with its modules. A node never has the controller in its default
/// context; this test process must not either, so the fixture names no controller or
/// wasmtime type (strings only) and is ignored when an earlier fixture has already loaded
/// them. Run it on its own for the real verdict:
///   dotnet test --filter FullyQualifiedName~VerifyModuleLoadTests
/// Opt-in: the native library is staged from this test's bin and the language runtimes from
/// @Data/runtimes (download-spike-runtimes.ps1), both only for the duration of the fixture.
/// </summary>
[TestFixture]
[Order(0)]
public class VerifyModuleLoadTests
{
    #region Constants

    private const string VERIFY_ASSEMBLY = "OutWit.Controller.AI.Verify";
    private const string WASMTIME_ASSEMBLY = "Wasmtime.Dotnet";
    private const string SELF_CHECK_TYPE = "OutWit.Controller.AI.Verify.Sandbox.VerifySelfCheck";
    private const string SELF_CHECK_METHOD = "Run";
    private const string RUNTIMES_OVERRIDE_ENV = "WITAI_VERIFY_RUNTIMES";

    private const string NATIVE_LOADED = "wasmtime-native: loaded";
    private const string MANAGED_PREFIX = "wasmtime-managed: ";
    private const string WASM_ROOT_PREFIX = "wasm-root: ";
    private const string NATIVE_PATH_PREFIX = "wasmtime-native-path: ";
    private const string PYTHON_PASS = "python-3.14.6: Pass exit=0 stdout=42";
    private const string SYMPY_PASS = "python-3.14.6-sympy-1.14.0: Pass exit=0 stdout=1.14.0";
    private const string QUICKJS_PASS = "quickjs-0.15.1: Pass exit=0 stdout=42";
    private const string WASI_PASS = "wasi: Pass exit=0 stdout=hello";

    #endregion

    #region Fields

    private string m_controllersPath = null!;
    private string m_moduleDir = null!;
    private string m_nativeDir = null!;
    private string m_wasmDir = null!;
    private string? m_savedOverride;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        // The Model is not on this list: NUnit's discovery pulls it into the default context
        // through the signatures of other fixtures, so the loader shares it from the test bin.
        // That does not weaken the proof - the Model carries no native code and the module's
        // copy is byte-identical; what must come from the module are the controller, the
        // managed wasmtime binding and the native library.
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .Where(n => n == VERIFY_ASSEMBLY || n == WASMTIME_ASSEMBLY)
            .ToList();
        if (alreadyLoaded.Count > 0)
            Assert.Ignore($"{string.Join(", ", alreadyLoaded)} already in the default load context (an earlier fixture); " +
                          "run this fixture alone for the isolation proof.");

        var solutionRoot = FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        var fixtureRuntimes = Path.Combine(solutionRoot, "AI", "OutWit.Controller.AI.Verify.Tests", "@Data", "runtimes");
        if (!Directory.Exists(Path.Combine(fixtureRuntimes, "python-3.14.6")) ||
            !File.Exists(Path.Combine(fixtureRuntimes, "qjs-wasi.wasm")))
            Assert.Ignore($"Pinned runtimes not found under '{fixtureRuntimes}' — run verify-assets build @Data/runtimes to opt in.");

        m_controllersPath = Path.Combine(solutionRoot, "@Controllers", "Debug");
        m_moduleDir = Path.Combine(m_controllersPath, "verify.module");
        if (!File.Exists(Path.Combine(m_moduleDir, "controller.json")))
            Assert.Ignore($"Staged module not found at '{m_moduleDir}'");

        // The binding is staged by the build (ControllerModuleAssembly); its absence is a
        // staging regression, not a missing prerequisite.
        Assert.That(File.Exists(Path.Combine(m_moduleDir, $"{WASMTIME_ASSEMBLY}.dll")), Is.True,
            $"{WASMTIME_ASSEMBLY}.dll must be staged into verify.module by the controller build");

        var rid = RuntimeInformation.RuntimeIdentifier;
        var binNative = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native");
        if (!Directory.Exists(binNative))
            Assert.Ignore($"No native wasmtime for '{rid}' in the test bin ('{binNative}')");

        m_nativeDir = Path.Combine(m_moduleDir, "runtimes", rid, "native");
        CopyTree(binNative, m_nativeDir);

        m_wasmDir = Path.Combine(m_moduleDir, "wasm");
        CopyTree(fixtureRuntimes, m_wasmDir);

        // The module must find its runtimes next to the controller, not through the override.
        m_savedOverride = Environment.GetEnvironmentVariable(RUNTIMES_OVERRIDE_ENV);
        Environment.SetEnvironmentVariable(RUNTIMES_OVERRIDE_ENV, null);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (m_controllersPath == null)
            return;

        Environment.SetEnvironmentVariable(RUNTIMES_OVERRIDE_ENV, m_savedOverride);

        // The language runtimes are plain files; the native library may stay mapped into
        // this process after the context unloads, so its removal is best-effort.
        TryDelete(m_wasmDir);
        TryDelete(m_nativeDir);
    }

    #endregion

    #region Module Load Tests

    [Test]
    public void StagedModuleRunsSandboxFromIsolatedContextTest()
    {
        using var loader = new WitPluginLoader<IWitControllerNode>(m_controllersPath, useIsolatedContexts: true);
        loader.Load();

        var plugin = loader.Plugins.FirstOrDefault(p => p.GetType().Assembly.GetName().Name == VERIFY_ASSEMBLY);
        Assert.That(plugin, Is.Not.Null, "verify.module did not load through the plugin loader");

        var pluginAssembly = plugin!.GetType().Assembly;
        Assert.That(pluginAssembly.Location, Does.StartWith(m_moduleDir).IgnoreCase,
            "the controller must come from the staged module, not from the test bin");

        var selfCheck = pluginAssembly.GetType(SELF_CHECK_TYPE);
        Assert.That(selfCheck, Is.Not.Null, $"{SELF_CHECK_TYPE} not found in the loaded module");

        var run = selfCheck!.GetMethod(SELF_CHECK_METHOD, BindingFlags.Public | BindingFlags.Static);
        Assert.That(run, Is.Not.Null, $"{SELF_CHECK_TYPE}.{SELF_CHECK_METHOD} not found");

        var report = (string)run!.Invoke(null, null)!;
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain(NATIVE_LOADED), report);
            Assert.That(report, Does.Contain(PYTHON_PASS), report);
            Assert.That(report, Does.Contain(SYMPY_PASS), report);
            Assert.That(report, Does.Contain(QUICKJS_PASS), report);
            Assert.That(report, Does.Contain(WASI_PASS), report);
            Assert.That(ReportValue(report, MANAGED_PREFIX), Does.StartWith(m_moduleDir).IgnoreCase,
                "Wasmtime.Dotnet must resolve from the module, not from the test bin");
            Assert.That(ReportValue(report, WASM_ROOT_PREFIX), Is.EqualTo(m_wasmDir).IgnoreCase,
                "the language runtimes must be found in the module's wasm/ folder");
            Assert.That(ReportValue(report, NATIVE_PATH_PREFIX), Does.StartWith(m_moduleDir).IgnoreCase,
                "the module-aware resolver must point at the module's runtimes/<rid>/native copy");
            Assert.That(LoadedNativeModulesUnder(m_moduleDir), Is.Not.Empty,
                "the native wasmtime library must be the module's copy, not the test bin's");
        });
    }

    #endregion

    #region Tools

    private static string ReportValue(string report, string prefix)
    {
        var line = report.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line == null ? "" : line.Substring(prefix.Length);
    }

    private static IReadOnlyList<string> LoadedNativeModulesUnder(string folder)
    {
        return Process.GetCurrentProcess().Modules
            .Cast<ProcessModule>()
            .Select(m => m.FileName)
            .Where(f => f.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static void TryDelete(string? dir)
    {
        if (dir == null || !Directory.Exists(dir))
            return;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Mapped native library: left in place, overwritten on the next run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OutWit.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    #endregion
}
