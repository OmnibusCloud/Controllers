using System.Diagnostics;
using System.Reflection;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The client-like host (OutWit.Controller.AI.Verify.Tests.ClientHost) as the shared-context tests
/// run it: a folder of controllers loaded into one shared load context, optionally after a first
/// carrier of the wasm host, and Verify's self-check printed. Also the staged modules those
/// tests copy from. Every run is a process of its own: nothing of a controller enters the test
/// process.
/// </summary>
internal static class WasmHostClientHost
{
    #region Constants

    /// <summary>The staged Verify module's folder name.</summary>
    public const string VERIFY_MODULE = "verify.module";

    /// <summary>The module Verify depends on, which the loader wants beside it.</summary>
    public const string DEPENDENCY_MODULE = "variables.module";

    /// <summary>The binding's file.</summary>
    public const string BINDING_FILE = "Wasmtime.Dotnet.dll";

    private const string HOST = "OutWit.Controller.AI.Verify.Tests.ClientHost";
    private const string CARRIER = "OutWit.Controller.AI.Verify.Tests.Carrier";
    private const string CARRIER_TYPE = "OutWit.Controller.AI.Verify.Tests.Carrier.CarrierProbe";
    private const string CARRIER_METHOD = "Run";
    private const string CI_ENV = "GITHUB_ACTIONS";
    private const int TIMEOUT_MS = 180_000;

    #endregion

    #region Functions

    /// <summary>The staged verify.module of this configuration; Assert.Ignore locally when the build staged none, a failure on the build server.</summary>
    /// <returns>Its folder.</returns>
    public static string StagedVerifyModule()
    {
        var staged = Path.Combine(ControllersFolder(), VERIFY_MODULE);
        if (!File.Exists(Path.Combine(staged, BINDING_FILE)))
        {
            if (string.Equals(Environment.GetEnvironmentVariable(CI_ENV), "true", StringComparison.OrdinalIgnoreCase))
                Assert.Fail($"no staged {VERIFY_MODULE} at '{staged}'");

            Assert.Ignore($"Staged module not found at '{staged}'");
        }

        return staged;
    }

    /// <summary>
    /// Lays out a controllers folder with a copy of the staged verify.module (its own files, no
    /// natives and no language runtimes) and the module it depends on.
    /// </summary>
    /// <param name="controllers">The folder to make.</param>
    /// <returns>The copy of verify.module.</returns>
    public static string CopyVerifyModule(string controllers)
    {
        var staged = StagedVerifyModule();
        var module = Path.Combine(controllers, VERIFY_MODULE);
        Directory.CreateDirectory(module);
        foreach (var file in Directory.GetFiles(staged))
            File.Copy(file, Path.Combine(module, Path.GetFileName(file)));

        CopyTree(Path.Combine(Path.GetDirectoryName(staged)!, DEPENDENCY_MODULE), Path.Combine(controllers, DEPENDENCY_MODULE));
        return module;
    }

    /// <summary>
    /// Lays out a second carrier of the host: the carrier assembly next to its binding, nothing else.
    /// </summary>
    /// <param name="folder">The folder to make.</param>
    /// <returns>The carrier assembly's path.</returns>
    public static string CopyCarrier(string folder)
    {
        var bin = Path.Combine(AiRoot(), CARRIER, "bin", Configuration(), Framework());
        var assembly = Path.Combine(bin, $"{CARRIER}.dll");
        Assert.That(File.Exists(assembly), Is.True, $"the carrier is built with the tests: {assembly}");

        Directory.CreateDirectory(folder);
        File.Copy(assembly, Path.Combine(folder, Path.GetFileName(assembly)));
        File.Copy(Path.Combine(bin, BINDING_FILE), Path.Combine(folder, BINDING_FILE));
        return Path.Combine(folder, Path.GetFileName(assembly));
    }

    /// <summary>
    /// The native library the <c>Wasmtime</c> package brings (48.0.2, not the pinned patch): the
    /// carrier's build output keeps the package's own copy.
    /// </summary>
    /// <returns>Its path.</returns>
    public static string PackageLibrary()
    {
        var bin = Path.Combine(AiRoot(), CARRIER, "bin", Configuration(), Framework());
        var library = Path.Combine(bin, "runtimes", OutWit.Controller.AI.Wasm.WasmHostNativeLibrary.PortableRid, "native", OutWit.Controller.AI.Wasm.WasmHostNativeLibrary.FileName);
        Assert.That(File.Exists(library), Is.True, $"the carrier's output keeps the package's library: {library}");
        return library;
    }

    /// <summary>Copies the library at a path into a module's <c>runtimes/&lt;rid&gt;/native/</c>.</summary>
    /// <param name="library">The library file.</param>
    /// <param name="module">The module folder.</param>
    /// <returns>The copy's path.</returns>
    public static string AddNatives(string library, string module)
    {
        var folder = Path.Combine(module, "runtimes", OutWit.Controller.AI.Wasm.WasmHostNativeLibrary.PortableRid, "native");
        Directory.CreateDirectory(folder);
        var copy = Path.Combine(folder, Path.GetFileName(library));
        File.Copy(library, copy, overwrite: true);
        return copy;
    }

    /// <summary>
    /// Runs the client-like host on a controllers folder: the first carrier's probe (when given),
    /// then the controllers in one shared context and Verify's self-check.
    /// </summary>
    /// <param name="controllers">The controllers folder.</param>
    /// <param name="carrier">The first carrier's assembly, or null for none.</param>
    /// <returns>The exit code and everything the host printed.</returns>
    public static (int ExitCode, string Output) Run(string controllers, string? carrier = null)
    {
        var bin = Path.Combine(AiRoot(), HOST, "bin", Configuration(), Framework());
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? Path.Combine(bin, $"{HOST}.exe") : "dotnet");
        if (!OperatingSystem.IsWindows())
            start.ArgumentList.Add(Path.Combine(bin, $"{HOST}.dll"));
        start.ArgumentList.Add(controllers);
        if (carrier != null)
        {
            start.ArgumentList.Add(carrier);
            start.ArgumentList.Add(CARRIER_TYPE);
            start.ArgumentList.Add(CARRIER_METHOD);
        }

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

    /// <summary>The value of the report's first line with a prefix, or an empty string.</summary>
    /// <param name="report">The report.</param>
    /// <param name="prefix">The line's prefix.</param>
    /// <returns>The rest of the line.</returns>
    public static string Value(string report, string prefix)
    {
        var line = report.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line == null ? "" : line[prefix.Length..];
    }

    /// <summary>Copies a folder tree.</summary>
    /// <param name="source">The folder to copy.</param>
    /// <param name="destination">Where the copy goes.</param>
    public static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private static string ControllersFolder()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.slnx")))
            directory = directory.Parent;
        if (directory == null)
            Assert.Ignore("Solution root not found");

        var configuration = typeof(WasmHostClientHost).Assembly
            .GetCustomAttributes(typeof(AssemblyConfigurationAttribute), false)
            .OfType<AssemblyConfigurationAttribute>()
            .FirstOrDefault()?.Configuration ?? "Debug";
        return Path.Combine(directory!.FullName, "@Controllers", configuration);
    }

    private static string AiRoot()
    {
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
    }

    private static string Configuration()
    {
        return Path.GetFileName(Path.GetDirectoryName(TestContext.CurrentContext.TestDirectory)!);
    }

    private static string Framework()
    {
        return Path.GetFileName(TestContext.CurrentContext.TestDirectory);
    }

    #endregion
}
