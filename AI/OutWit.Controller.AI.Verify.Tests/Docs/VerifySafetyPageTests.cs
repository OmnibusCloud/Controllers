using System.Text.RegularExpressions;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Docs;

/// <summary>
/// The page for someone who lends a computer to the network (docs/safety.md) says what the
/// code does: its numbers are the code's constants, the engine it names is the one the
/// controller is built with, and the tests it points to exist. A limit changed in the code
/// without the page fails here.
/// </summary>
[TestFixture]
public sealed class VerifySafetyPageTests
{
    #region Constants

    private const string PAGE = "safety.md";
    private const string CONTROLLER_PROJECT = "OutWit.Controller.AI.Verify.csproj";
    private const string ENGINE_REFERENCE = "<PackageReference Include=\"Wasmtime\" Version=\"(?<version>[0-9.]+)\" />";
    private const string NATIVE_PINS = "pins.env";
    private const string NATIVE_VERSION = "^WASMTIME_NATIVE_VERSION=(?<version>[0-9.]+)\\s*$";

    private const long KIBIBYTE = 1024;
    private const long MEBIBYTE = 1024 * KIBIBYTE;
    private const long GIBIBYTE = 1024 * MEBIBYTE;
    private const long THOUSAND_MILLION = 1_000_000_000;

    private static readonly string[] NAMED_TESTS =
    [
        nameof(Sandbox.VerifyAdversarialTests),
        nameof(Sandbox.VerifyWasiRuntimeTests),
        nameof(Sandbox.VerifySandboxHygieneTests),
        nameof(Wasm.WasmHostNativeResolutionTests),
        nameof(Sandbox.VerifyNodeProtectionTests),
        nameof(Sandbox.VerifyHostProtectionTests),
        nameof(Sandbox.VerifyClangRuntimeTests),
        nameof(Sandbox.VerifyTrapModeTests),
        nameof(Sandbox.VerifyHostileKitTests),
        nameof(Adapters.VerifyLoggerFateTests)
    ];

    #endregion

    #region Tests

    [Test]
    public void ThePageStatesTheLimitsTheCodeHoldsTest()
    {
        var page = Load(PAGE);

        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain($"{VerifySandboxDefaults.DEFAULT_FUEL_BUDGET / THOUSAND_MILLION} thousand million by default"));
            Assert.That(page, Does.Contain($"never more than {VerifyLimitCeilings.MAX_FUEL_BUDGET / THOUSAND_MILLION} thousand million"));
            Assert.That(page, Does.Contain($"about {VerifySandboxDefaults.DEFAULT_FUEL_BUDGET / VerifySandboxDefaults.REFERENCE_FUEL_PER_SECOND} seconds"));
            Assert.That(page, Does.Contain($"after {VerifyBackstop.WallMs(VerifySandboxDefaults.DEFAULT_FUEL_BUDGET) / 1000} seconds for the default budget"));
            Assert.That(page, Does.Contain($"a little over {VerifyBackstop.WallMs(VerifyLimitCeilings.MAX_FUEL_BUDGET) / 60_000} minutes for the largest"));
            Assert.That(page, Does.Contain($"{VerifySandboxDefaults.DEFAULT_MEMORY_BYTES / MEBIBYTE} MB by default"));
            Assert.That(page, Does.Contain($"never more than {VerifyLimitCeilings.MAX_MEMORY_BYTES / GIBIBYTE} GB"));
            Assert.That(page, Does.Contain($"{VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES / KIBIBYTE} KB by default"));
            Assert.That(page, Does.Contain($"never more than {VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES / MEBIBYTE} MB"));
            Assert.That(page, Does.Contain($"{VerifySandboxDegree.DEFAULT_MAX_DEGREE} at most"));
            Assert.That(page, Does.Contain($"and {VerifyLimitCeilings.MAX_MEMORY_BYTES / GIBIBYTE} GB for a run"));
            Assert.That(page, Does.Contain($"{VerifyClangBuild.STEP_MEMORY_BYTES / GIBIBYTE} GB of memory a step"));
            Assert.That(page, Does.Contain($"{VerifyClangBuild.PRODUCT_LIMIT_BYTES / MEBIBYTE} MB"));
            Assert.That(page, Does.Contain($"all of one build together at {VerifyClangBuild.BUILD_LIMIT_BYTES / MEBIBYTE} MB"));
            Assert.That(page, Does.Contain($"Compiles that may take {VerifyWasmSandbox.HEAVY_COMPILE_BYTES / MEBIBYTE} MB or more share 2.5 GB between them"));
            Assert.That(page, Does.Contain("a module whose compile may take more than 2.5 GB is"));
            Assert.That(page, Does.Contain($"{VerifyWasmSandbox.HEAVY_COMPILE_BYTES / MEBIBYTE} MB a program, and about 2.8 GB set aside"));
            Assert.That(VerifyBatchExecutor.MemoryAside(VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out _)!), Is.InRange(2750 * MEBIBYTE, 2900 * MEBIBYTE), "the page says about 2.8 GB");
            Assert.That(VerifyWasmSandbox.CompileGateBytes, Is.EqualTo(2560 * MEBIBYTE), "the page says 2.5 GB, three times");
            Assert.That(page, Does.Contain($"{VerifyTaskModuleCache.DEFAULT_BYTE_CAPACITY / MEBIBYTE} MB of it at most"));
            Assert.That(page, Does.Contain("a thousand units a request, and one unit a byte it moves"));
            Assert.That(OutWit.Controller.AI.Wasm.WasmHostMeter.CALL_FUEL, Is.EqualTo(1000), "the page writes this number as a word: a thousand");
            Assert.That(page, Does.Contain($"blocks nested deeper than {VerifyModuleShape.MAX_NESTING.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}"));
            Assert.That(page, Does.Contain("to within a few times either way"));
            Assert.That(page, Does.Contain("it is not a proof about every module"));

            // What a task may hold, and what a node refuses a package for.
            var cases = OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_SUITE_CASES.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_SOURCES, Is.EqualTo(OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_SUITE_CASES), "the page says one number for files, arguments and cases");
            Assert.That(OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_ARGS, Is.EqualTo(OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_SUITE_CASES));
            Assert.That(page, Does.Contain($"is run once for each case, {cases} cases at most, and every run has the budget"));
            Assert.That(page, Does.Contain($"({cases} of each)"));
            Assert.That(page, Does.Contain($"as many runs as it has cases, {cases} at most"));
            Assert.That(page, Does.Contain($"({OutWit.Controller.AI.Verify.Delivery.VerifyPackagePlanner.MAX_PACKAGE_TASKS.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}"));

            // The clock, where the page states it a second time.
            Assert.That(page, Does.Contain($"({VerifyBackstop.WallMs(VerifySandboxDefaults.DEFAULT_FUEL_BUDGET) / 1000} seconds for the default budget, a little over {VerifyBackstop.WallMs(VerifyLimitCeilings.MAX_FUEL_BUDGET) / 60_000} minutes for the largest)"));

            // A transfer's time, and a ticket's size.
            Assert.That(page, Does.Contain($"at {OutWit.Controller.AI.Verify.Delivery.VerifyPackageTransfer.TRANSFER_SLOWEST_BYTES_PER_SECOND / KIBIBYTE} KB a second"));
            Assert.That(page, Does.Contain($"{OutWit.Controller.AI.Verify.Delivery.VerifyTickets.TICKET_BYTES} random bytes"));
        });
    }

    [Test]
    public void ThePageStatesTheShareOfMemoryTheCodeTakesTest()
    {
        Assert.That(VerifySandboxDegree.DEFAULT_RAM_HEADROOM, Is.EqualTo(0.75));
        Assert.That(Load(PAGE), Does.Contain("three quarters of the memory that is free"));
    }

    [Test]
    public void ThePageNamesTheEngineTheControllerIsBuiltWithTest()
    {
        // The binding is the package the controller references; the native library is the
        // one the asset tool pins and the data assets carry.
        var project = Load(Path.Combine("..", CONTROLLER_PROJECT));
        var binding = Regex.Match(project, ENGINE_REFERENCE);
        Assert.That(binding.Success, Is.True, "the controller project names its wasmtime package");

        var pins = Load(Path.Combine("..", "..", "OutWit.Controller.AI.Verify.Assets", "toolchain", NATIVE_PINS));
        var native = Regex.Match(pins, NATIVE_VERSION, RegexOptions.Multiline);
        Assert.That(native.Success, Is.True, "the asset tool pins the native library's version");

        var page = Load(PAGE);
        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain($"binding wasmtime {binding.Groups["version"].Value}"));
            Assert.That(page, Does.Contain($"native library of wasmtime {native.Groups["version"].Value}"));
            Assert.That(binding.Groups["version"].Value.Split('.')[0], Is.EqualTo(native.Groups["version"].Value.Split('.')[0]),
                "the binding and the native library are of one line");
        });
    }

    [TestCaseSource(nameof(NAMED_TESTS))]
    public void ThePageNamesTestsThatExistTest(string fixture)
    {
        Assert.That(Load(PAGE), Does.Contain($"`{fixture}`"));
    }

    #endregion

    #region Tools

    private static string Load(string relativePath)
    {
        var root = FindSolutionRoot();
        if (root == null)
            Mock.VerifyRigModules.Missing("Solution root not found");

        var path = Path.GetFullPath(Path.Combine(root, "AI", "OutWit.Controller.AI.Verify", "docs", relativePath));
        Assert.That(File.Exists(path), Is.True, $"missing: {path}");
        return File.ReadAllText(path);
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
}
