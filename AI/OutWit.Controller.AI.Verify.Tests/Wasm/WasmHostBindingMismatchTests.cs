using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The binding guard where it matters: the cloud client's one shared load context, where the
/// binding assembly loads once and the first controller to load it decides its version. The staged
/// verify.module is copied with its <c>Wasmtime.Dotnet</c> re-versioned (the same code under
/// another assembly version, as a controller built against another binding would bring), loaded
/// by a host built as the cloud client is (OutWit.Controller.AI.Verify.Tests.ClientHost), and the
/// self-check must fail with the guard's words naming both versions — not run on a binding it was
/// never built against. A separate process: nothing of the controller enters this test process.
/// </summary>
[TestFixture]
public sealed class WasmHostBindingMismatchTests
{
    #region Constants

    private const string HOST = "OutWit.Controller.AI.Verify.Tests.ClientHost";
    private const string MODULE = "verify.module";
    private const string DEPENDENCY_MODULE = "variables.module";
    private const string BINDING_FILE = "Wasmtime.Dotnet.dll";
    private const string BUILT_AGAINST = WasmHostBinding.BUILT_AGAINST;
    private const string CI_ENV = "GITHUB_ACTIONS";
    private const int TIMEOUT_MS = 180_000;

    // The Assembly table row: HashAlgId (4 bytes), then MajorVersion, MinorVersion, BuildNumber (2 each).
    private const int BUILD_NUMBER_OFFSET = 4 + 2 + 2;

    #endregion

    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_binding_mismatch_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch (IOException)
        {
            // the host process may still hold a file for a moment; the temp folder goes later
        }
    }

    #endregion

    #region Tests

    [Test]
    public void AControllerOnAnotherBindingFailsItsSelfCheckNamingBothVersionsTest()
    {
        var staged = StagedModule();
        var controllers = Path.Combine(m_root, "controllers");
        var module = Path.Combine(controllers, MODULE);
        Directory.CreateDirectory(module);

        // The module's own files; its natives and language runtimes are not needed: the guard
        // stops the sandbox before anything native is called. The loader wants the module's
        // controller dependency (Variables) next to it.
        foreach (var file in Directory.GetFiles(staged))
            File.Copy(file, Path.Combine(module, Path.GetFileName(file)));
        CopyTree(Path.Combine(Path.GetDirectoryName(staged)!, DEPENDENCY_MODULE), Path.Combine(controllers, DEPENDENCY_MODULE));

        // The same code under another patch number of the binding's version.
        var builtAgainst = Version.Parse(BUILT_AGAINST);
        var binding = Path.Combine(module, BINDING_FILE);
        SetBuildNumber(binding, (ushort)(builtAgainst.Build + 7));
        var foreign = AssemblyName.GetAssemblyName(binding).Version!;
        Assert.That(foreign, Is.EqualTo(new Version(builtAgainst.Major, builtAgainst.Minor, builtAgainst.Build + 7, 0)), "the binding was re-versioned");

        var (exitCode, report) = RunClientHost(controllers);
        TestContext.Out.WriteLine(report);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(0), report);
            Assert.That(report, Does.Contain($"wasmtime-managed: {binding}").IgnoreCase, "the controller runs on the re-versioned binding");
            Assert.That(report, Does.Contain("wasmtime-native: FAILED InvalidOperationException: " +
                                             $"the wasmtime binding loaded in this process is Wasmtime.Dotnet {foreign}, but this controller was built against {BUILT_AGAINST}"), report);
            Assert.That(report, Does.Not.Contain("wasmtime-native: loaded"), report);
        });
    }

    #endregion

    #region Tools

    private static string StagedModule()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;
        if (directory == null)
            Assert.Ignore("Solution root not found");

        var configuration = typeof(WasmHostBindingMismatchTests).Assembly
            .GetCustomAttributes(typeof(AssemblyConfigurationAttribute), false)
            .OfType<AssemblyConfigurationAttribute>()
            .FirstOrDefault()?.Configuration ?? "Debug";
        var staged = Path.Combine(directory!.FullName, "@Controllers", configuration, MODULE);
        if (!File.Exists(Path.Combine(staged, BINDING_FILE)))
        {
            // The build stages the module with the tests; on the build server its absence is a regression.
            if (string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase))
                Assert.Fail($"no staged {MODULE} at '{staged}'");

            Assert.Ignore($"Staged module not found at '{staged}'");
        }

        return staged;
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    /// <summary>Rewrites the build number of an assembly's version in place, in its metadata.</summary>
    private static void SetBuildNumber(string path, ushort build)
    {
        var bytes = File.ReadAllBytes(path);
        int offset;
        using (var pe = new PEReader(new MemoryStream(bytes)))
        {
            var metadata = pe.GetMetadataReader();
            offset = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.Assembly) + BUILD_NUMBER_OFFSET;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)), build);
        File.WriteAllBytes(path, bytes);
    }

    private static (int ExitCode, string Output) RunClientHost(string controllersPath)
    {
        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var framework = Path.GetFileName(testDirectory);
        var configuration = Path.GetFileName(Path.GetDirectoryName(testDirectory)!);
        var aiRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "..", ".."));
        var bin = Path.Combine(aiRoot, HOST, "bin", configuration, framework);
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? Path.Combine(bin, $"{HOST}.exe") : "dotnet");
        if (!OperatingSystem.IsWindows())
            start.ArgumentList.Add(Path.Combine(bin, $"{HOST}.dll"));
        start.ArgumentList.Add(controllersPath);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("the client-like host did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TIMEOUT_MS))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the client-like host did not finish");
        }

        return (process.ExitCode, output.Result + error.Result);
    }

    #endregion
}
