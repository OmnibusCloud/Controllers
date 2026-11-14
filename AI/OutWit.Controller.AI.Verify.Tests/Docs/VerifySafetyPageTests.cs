using System.Text.RegularExpressions;
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

    private static readonly string[] NAMED_TESTS =
    [
        nameof(Sandbox.VerifyAdversarialTests),
        nameof(Sandbox.VerifyWasiRuntimeTests),
        nameof(Sandbox.VerifySandboxHygieneTests),
        nameof(Wasm.WasmHostNativeResolutionTests)
    ];

    #endregion

    #region Tests

    [Test]
    public void ThePageStatesTheLimitsTheCodeHoldsTest()
    {
        var page = Load(PAGE);

        Assert.Multiple(() =>
        {
            Assert.That(page, Does.Contain($"{VerifySandboxDefaults.DEFAULT_WALL_TIME_MS / 1000} seconds by default"));
            Assert.That(page, Does.Contain($"never more than {VerifyLimitCeilings.MAX_WALL_TIME_MS / 60_000} minutes"));
            Assert.That(page, Does.Contain($"{VerifySandboxDefaults.DEFAULT_MEMORY_BYTES / MEBIBYTE} MB by default"));
            Assert.That(page, Does.Contain($"never more than {VerifyLimitCeilings.MAX_MEMORY_BYTES / GIBIBYTE} GB"));
            Assert.That(page, Does.Contain($"cut at {VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES / KIBIBYTE} KB by default"));
            Assert.That(page, Does.Contain($"{VerifySandboxDegree.DEFAULT_MAX_DEGREE} at most unless the lab asks"));
            Assert.That(page, Does.Contain($"up to {VerifyLimitCeilings.MAX_WALL_TIME_MS / 60_000} minutes"));
            Assert.That(page, Does.Contain($"and {VerifyLimitCeilings.MAX_MEMORY_BYTES / GIBIBYTE} GB a program"));
        });
    }

    [Test]
    public void ThePageStatesTheShareOfMemoryTheCodeTakesTest()
    {
        Assert.That(VerifySandboxDegree.DEFAULT_RAM_HEADROOM, Is.EqualTo(0.75));
        Assert.That(Load(PAGE), Does.Contain("three quarters of your RAM"));
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
            Assert.Ignore("Solution root not found");

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
