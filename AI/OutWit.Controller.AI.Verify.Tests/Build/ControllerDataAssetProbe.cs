using System.Diagnostics;
using System.Text;

namespace OutWit.Controller.AI.Verify.Tests.Build;

/// <summary>
/// A controller project of its own in the temp folder, made by the repository's shared build
/// files (<c>Build/OutWit.Controller.*</c>), that declares one data asset whose SHA-256 and
/// size are set from the command line. <see cref="Run"/> runs the target that writes the
/// controller's manifest (<c>GenerateControllerBuildArtifacts</c>, which every build of a
/// controller runs before it compiles) and nothing else, so no restore and no compile are
/// needed and a case takes seconds. It is outside the repository: nothing it writes reaches
/// the repository's own modules.
/// </summary>
internal sealed class ControllerDataAssetProbe : IDisposable
{
    #region Constants

    public const string ASSET = "probe-tool";

    private const string CONTROLLER = "OutWit.Controller.AssetProbe";
    private const string TARGET = "GenerateControllerBuildArtifacts";
    private const string MANIFEST = "controller.json";
    private const int RUN_TIMEOUT_MS = 300_000;

    // What a build started by "dotnet test" hands its children and a nested build must not take.
    private static readonly string[] INHERITED = ["MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildLoadMicrosoftTargetsReadOnly", "MSBUILDUSESERVER"];

    #endregion

    #region Fields

    private readonly string m_root;
    private readonly string m_project;

    #endregion

    #region Constructors

    public ControllerDataAssetProbe(string repositoryRoot)
    {
        m_root = Path.Combine(Path.GetTempPath(), "wit-asset-pin-" + Guid.NewGuid().ToString("N")[..8]);
        m_project = Path.Combine(m_root, "AssetProbe", CONTROLLER);

        var build = Path.Combine(repositoryRoot, "Build");

        Write(Path.Combine(m_root, "OutWit.slnx"), "<Solution />\n");

        // The SHA-256 and the size come from the command line; "ProbeNoSize=true" leaves the
        // Size out of the item, as a project file that does not write one.
        Write(Path.Combine(m_project, CONTROLLER + ".csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <Import Project="{Path.Combine(build, "OutWit.Controller.props")}" />
               <PropertyGroup>
                 <ControllerName>AssetProbe</ControllerName>
                 <Version>1.0.0</Version>
                 <Description>A controller of the build's own tests that declares one data asset.</Description>
                 <PackageTags>test</PackageTags>
               </PropertyGroup>
               <ItemGroup>
                 <ControllerDataAsset Include="{ASSET}">
                   <Uri>gh-release://example/example/probe-v1/probe-tool.zip</Uri>
                   <Sha256>$(ProbeSha256)</Sha256>
                   <Size Condition="'$(ProbeNoSize)' != 'true'">$(ProbeSize)</Size>
                 </ControllerDataAsset>
               </ItemGroup>
               <Import Project="{Path.Combine(build, "OutWit.Controller.targets")}" />
             </Project>
             """);
    }

    #endregion

    #region Functions

    /// <summary>Writes the controller's manifest with the asset pinned as given.</summary>
    /// <param name="sha256">The asset's <c>Sha256</c>.</param>
    /// <param name="size">The asset's <c>Size</c>; <see langword="null"/> leaves it out of the item.</param>
    /// <returns>The exit code of the build and its output.</returns>
    public (int ExitCode, string Output) Run(string sha256, string? size)
    {
        // The last manifest must not be read for this one.
        if (File.Exists(Manifest))
            File.Delete(Manifest);

        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            WorkingDirectory = m_root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in new[] { "msbuild", Path.Combine(m_project, CONTROLLER + ".csproj"), "-t:" + TARGET, "-nologo", "-v:minimal", "-nr:false", "-p:ProbeSha256=" + sha256 })
            start.ArgumentList.Add(argument);

        start.ArgumentList.Add(size == null ? "-p:ProbeNoSize=true" : "-p:ProbeSize=" + size);

        foreach (var name in INHERITED)
            start.Environment.Remove(name);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(RUN_TIMEOUT_MS))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"the probe's build did not end in {RUN_TIMEOUT_MS / 1000} s");
        }

        return (process.ExitCode, output.Result + errors.Result);
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

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    #endregion

    #region Properties

    /// <summary>The manifest the target writes (under the project's <c>obj</c>).</summary>
    public string Manifest => Path.Combine(m_project, "obj", "Debug", "net10.0", MANIFEST);

    #endregion
}
