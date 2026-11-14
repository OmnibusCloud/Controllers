using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// Reads the repository the tests run in: the AI projects, the <c>Wasmtime</c> package each
/// references, which of them compile the shared wasm host, and the engine pins of the asset tool.
/// </summary>
internal static class WasmHostRepository
{
    #region Constants

    /// <summary>The NuGet package of the binding.</summary>
    public const string WASMTIME_PACKAGE = "Wasmtime";

    private const string SOLUTION_FILE = "OutWit.slnx";
    private const string AI_FOLDER = "AI";
    private const string SHARED_HOST_FOLDER = "Shared\\Wasm";

    #endregion

    #region Functions

    /// <summary>The repository's AI folder.</summary>
    /// <returns>The full path.</returns>
    public static string AiRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, $"{SOLUTION_FILE} above the test folder");
        return Path.Combine(directory!.FullName, AI_FOLDER);
    }

    /// <summary>Every project file under the AI folder, build output left out.</summary>
    /// <returns>Path and parsed document of each, by path.</returns>
    public static IReadOnlyList<(string Path, XDocument Project)> AiProjects()
    {
        return Directory.GetFiles(AiRoot(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (path, XDocument.Load(path)))
            .ToList();
    }

    /// <summary>The version of the project's <c>Wasmtime</c> PackageReference.</summary>
    /// <param name="project">The project file.</param>
    /// <returns>The version, or null when the project does not reference the package.</returns>
    public static string? WasmtimeVersion(XDocument project)
    {
        return project.Descendants("PackageReference")
            .FirstOrDefault(item => string.Equals((string?)item.Attribute("Include"), WASMTIME_PACKAGE, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Version")?.Value;
    }

    /// <summary>Whether the project compiles the shared wasm host's sources.</summary>
    /// <param name="project">The project file.</param>
    /// <returns>True when a Compile item includes the shared folder.</returns>
    public static bool CarriesTheSharedHost(XDocument project)
    {
        return project.Descendants("Compile")
            .Select(item => ((string?)item.Attribute("Include") ?? "").Replace('/', '\\'))
            .Any(include => include.Contains(SHARED_HOST_FOLDER, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The runtime identifiers of the project's <c>wasmtime-*</c> data assets (the shipped natives).</summary>
    /// <param name="project">The project file.</param>
    /// <returns>The identifiers, in project order.</returns>
    public static IReadOnlyList<string> ShippedNativeRids(XDocument project)
    {
        return project.Descendants("ControllerDataAsset")
            .Where(item => ((string?)item.Attribute("Include") ?? "").StartsWith("wasmtime-", StringComparison.Ordinal))
            .Select(item => item.Element("RuntimeIdentifier")?.Value ?? "")
            .ToList();
    }

    /// <summary>The project's <c>wasmtime-*</c> data assets: runtime identifier, SHA-256 and size of each.</summary>
    /// <param name="project">The project file.</param>
    /// <returns>The assets, in project order.</returns>
    public static IReadOnlyList<(string Rid, string Sha256, string Size)> ShippedNatives(XDocument project)
    {
        return project.Descendants("ControllerDataAsset")
            .Where(item => ((string?)item.Attribute("Include") ?? "").StartsWith("wasmtime-", StringComparison.Ordinal))
            .Select(item => (item.Element("RuntimeIdentifier")?.Value ?? "", item.Element("Sha256")?.Value ?? "", item.Element("Size")?.Value ?? ""))
            .ToList();
    }

    /// <summary>A value of the asset tool's toolchain pins (<c>toolchain/pins.env</c>).</summary>
    /// <param name="name">The variable.</param>
    /// <returns>Its value.</returns>
    public static string Pin(string name)
    {
        var match = Regex.Match(File.ReadAllText(ToolchainFile("pins.env")), $@"^{Regex.Escape(name)}=(?<value>\S+)\s*$", RegexOptions.Multiline);
        Assert.That(match.Success, Is.True, $"pins.env names {name}");
        return match.Groups["value"].Value;
    }

    /// <summary>The platforms the asset tool's <c>toolchain/natives.sh</c> lays the native libraries out for.</summary>
    /// <returns>The runtime identifiers.</returns>
    public static IReadOnlyList<string> NativesScriptRids()
    {
        return Regex.Matches(File.ReadAllText(ToolchainFile("natives.sh")), @"^(?:NATIVES="")?(?<rid>[a-z]+-[a-z0-9]+)\|\$WASMTIME_NATIVE_", RegexOptions.Multiline)
            .Select(match => match.Groups["rid"].Value)
            .ToList();
    }

    private static string ToolchainFile(string name)
    {
        return Path.Combine(AiRoot(), "OutWit.Controller.AI.Verify.Assets", "toolchain", name);
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin") || parts.Contains("obj");
    }

    #endregion
}
