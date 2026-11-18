using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Build;

/// <summary>
/// A solution of its own in the temp folder, built by the repository's shared build files
/// (<c>Build/OutWit.Controller.*</c>): a controller "Probe" with a companion Model, two
/// resources, one extra module file and one declared data asset. <see cref="ControllerStagingTests"/>
/// puts files into its module by hand, builds it, takes sources away and builds it again.
/// It is outside the repository, so nothing it stages reaches the repository's own modules.
/// </summary>
internal sealed class ControllerStagingProbe : IDisposable
{
    #region Constants

    public const string MODULE = "probe.module";
    public const string CONTROLLER = "OutWit.Controller.Probe";
    public const string MODEL = "OutWit.Controller.Probe.Model";
    public const string CONFIGURATION = "Release";
    public const string VERSION = "1.0.0";
    public const string ASSET_FOLDER = "tools/any/";
    public const string EXTRA_FILE = "once.txt";
    public const string SWAPPED_FILE = "swapped.txt";
    public const string KEPT_RESOURCE = "Resources/kept.dat";
    public const string DROPPED_RESOURCE = "Resources/dropped.dat";

    private const string PACKAGE_MODULE = "content/module/";
    private const string MANIFEST = "controller.json";
    private const int BUILD_TIMEOUT_MS = 600_000;

    // What a build started by "dotnet test" hands its children and a nested build must not take.
    private static readonly string[] INHERITED = ["MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildLoadMicrosoftTargetsReadOnly", "MSBUILDUSESERVER"];

    #endregion

    #region Fields

    private readonly string m_root;
    private readonly string m_controller;

    #endregion

    #region Constructors

    public ControllerStagingProbe(string repositoryRoot)
    {
        m_root = Path.Combine(Path.GetTempPath(), "wit-staging-" + Guid.NewGuid().ToString("N")[..8]);
        m_controller = Path.Combine(m_root, "Probe", CONTROLLER);

        var build = Path.Combine(repositoryRoot, "Build");
        var model = Path.Combine(m_root, "Probe", MODEL);

        Write(Path.Combine(m_root, "OutWit.slnx"), "<Solution />\n");

        Write(Path.Combine(model, MODEL + ".csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <Import Project="{Path.Combine(build, "OutWit.Controller.Model.props")}" />
               <PropertyGroup>
                 <ControllerName>Probe</ControllerName>
                 <Version>{VERSION}</Version>
                 <Description>The data types of a controller of the build's own tests.</Description>
                 <PackageTags>test</PackageTags>
               </PropertyGroup>
               <Import Project="{Path.Combine(build, "OutWit.Controller.Model.targets")}" />
             </Project>
             """);
        Write(Path.Combine(model, "ProbeData.cs"), "namespace OutWit.Controller.Probe.Model;\n\npublic sealed class ProbeData\n{\n    public int Value { get; set; }\n}\n");

        Write(Path.Combine(m_controller, CONTROLLER + ".csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <Import Project="{Path.Combine(build, "OutWit.Controller.props")}" />
               <PropertyGroup>
                 <ControllerName>Probe</ControllerName>
                 <Version>{VERSION}</Version>
                 <Description>A controller of the build's own tests.</Description>
                 <PackageTags>test</PackageTags>
               </PropertyGroup>
               <ItemGroup>
                 <ProjectReference Include="..\{MODEL}\{MODEL}.csproj" />
               </ItemGroup>
               <ItemGroup>
                 <ControllerModuleFile Include="$(MSBuildProjectDirectory)\{EXTRA_FILE}" Condition="Exists('$(MSBuildProjectDirectory)\{EXTRA_FILE}')" />
                 <ControllerModuleFile Include="$(MSBuildProjectDirectory)\{SWAPPED_FILE}" Condition="Exists('$(MSBuildProjectDirectory)\{SWAPPED_FILE}')" />
                 <ControllerDataAsset Include="probe-tool">
                   <Uri>gh-release://example/example/probe-v1/probe-tool.zip</Uri>
                   <Sha256>0000000000000000000000000000000000000000000000000000000000000000</Sha256>
                   <Size>1</Size>
                   <ExtractTo>{ASSET_FOLDER}</ExtractTo>
                 </ControllerDataAsset>
               </ItemGroup>
               <Import Project="{Path.Combine(build, "OutWit.Controller.targets")}" />
             </Project>
             """);
        Write(Path.Combine(m_controller, "Probe.cs"), "namespace OutWit.Controller.Probe;\n\npublic static class Probe\n{\n    public static int Twice(Model.ProbeData data) => data.Value * 2;\n}\n");
        Write(Path.Combine(m_controller, EXTRA_FILE), "staged once\n");
        Write(Path.Combine(m_controller, SWAPPED_FILE), "staged once, then replaced in the module by hand\n");
        Write(Path.Combine(m_controller, "Resources", "kept.dat"), "a resource that stays\n");
        Write(Path.Combine(m_controller, "Resources", "dropped.dat"), "a resource that is taken away\n");

        // What every controller and Model project packs from beside its project file.
        foreach (var project in new[] { m_controller, model })
        {
            File.Copy(Path.Combine(repositoryRoot, "LICENSE"), Path.Combine(project, "LICENSE"));
            File.Copy(Path.Combine(repositoryRoot, "WitEngine-logo.png"), Path.Combine(project, "WitEngine-logo.png"));
            Write(Path.Combine(project, "README.md"), "A project of the build's own tests.\n");
        }
    }

    #endregion

    #region Functions

    /// <summary>Puts a file into the module by hand, as a person would: no build knows of it.</summary>
    /// <param name="inside">The path inside the module, forward slashes.</param>
    /// <param name="content">The text of the file.</param>
    public void PutByHand(string inside, string content)
    {
        Write(Path.Combine(Module, inside.Replace('/', Path.DirectorySeparatorChar)), content);
    }

    /// <summary>Takes a source file of the controller's project away, so that the next build no longer stages it.</summary>
    /// <param name="relative">The path inside the project folder, forward slashes.</param>
    public void TakeAway(string relative)
    {
        File.Delete(Path.Combine(m_controller, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>Builds the controller (and the Model it references) as the repository's controllers are built.</summary>
    /// <returns>The build's output.</returns>
    public string Build()
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            WorkingDirectory = m_root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[] { "build", Path.Combine(m_controller, CONTROLLER + ".csproj"), "-c", CONFIGURATION, "-nologo", "-v", "minimal", "-nr:false", "-p:UseSharedCompilation=false" })
            start.ArgumentList.Add(argument);

        foreach (var name in INHERITED)
            start.Environment.Remove(name);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(BUILD_TIMEOUT_MS))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"the probe's build did not end in {BUILD_TIMEOUT_MS / 1000} s");
        }

        var text = output.Result + errors.Result;
        Assert.That(process.ExitCode, Is.Zero, "the probe's build failed:\n" + text);
        return text;
    }

    /// <summary>What a build left: the folder, the records, the zip, the package and the stated digest.</summary>
    /// <returns>The state, read into memory.</returns>
    public ControllerStagingState Read()
    {
        var folder = Directory.GetFiles(Module, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Module, path).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);

        // No folder of records is a build that kept none: the tests say so, each in its own words.
        var records = (Directory.Exists(Records) ? Directory.GetFiles(Records, "*.txt") : [])
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, Listed, StringComparer.Ordinal);

        using var zip = ZipFile.OpenRead(Path.Combine(m_root, "@Zips", CONFIGURATION, "probe.zip"));
        using var package = ZipFile.OpenRead(Path.Combine(m_controller, "bin", CONFIGURATION, $"{CONTROLLER}.{VERSION}.nupkg"));

        var packed = package.Entries
            .Where(entry => entry.FullName.StartsWith(PACKAGE_MODULE, StringComparison.Ordinal) && !entry.FullName.EndsWith('/'))
            .ToDictionary(entry => Uri.UnescapeDataString(entry.FullName[PACKAGE_MODULE.Length..]), Hash, StringComparer.Ordinal);

        var manifest = folder[MANIFEST];
        const string key = "\"contentDigest\": \"";
        var at = manifest.IndexOf(key, StringComparison.Ordinal) + key.Length;

        return new ControllerStagingState(
            folder,
            records,
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray(),
            packed.Keys.Order(StringComparer.Ordinal).ToArray(),
            manifest[at..manifest.IndexOf('"', at)],
            Digest(packed));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(m_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a process that is ending: the temp folder is the system's to clear.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    #endregion

    #region Tools

    /// <summary>
    /// The content digest of the files a package ships, by its definition: every file but the
    /// manifest, by its path (ordinal); SHA-256 over, per file, the path, a line feed, the 32
    /// bytes of the file's SHA-256 and a line feed.
    /// </summary>
    private static string Digest(IReadOnlyDictionary<string, byte[]> files)
    {
        using var total = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, hash) in files.Where(file => file.Key != MANIFEST).OrderBy(file => file.Key, StringComparer.Ordinal))
        {
            total.AppendData(Encoding.UTF8.GetBytes(path));
            total.AppendData("\n"u8);
            total.AppendData(hash);
            total.AppendData("\n"u8);
        }

        return "sha256:" + Convert.ToHexStringLower(total.GetHashAndReset());
    }

    private static byte[] Hash(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return SHA256.HashData(stream);
    }

    /// <summary>The files a record lists: of its lines that are not comments, the path, which is what stands before the first tab.</summary>
    private static string[] Listed(string record)
    {
        return File.ReadAllLines(record).Where(line => line.Length > 0 && !line.StartsWith('#')).Select(line => line.Split('\t')[0]).ToArray();
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    #endregion

    #region Properties

    /// <summary>The controller's module folder.</summary>
    public string Module => Path.Combine(m_root, "@Controllers", CONFIGURATION, MODULE);

    /// <summary>The folder of the build's records, outside the module folders.</summary>
    public string Records => Path.Combine(m_root, "@Controllers", ".staged", CONFIGURATION);

    #endregion
}
