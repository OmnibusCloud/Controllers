using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The data assets a module's manifest declares, resolved the way a consumer of the package
/// resolves them: the archive the manifest names is fetched from the release it names, held to
/// the size and the SHA-256 the manifest states, and unpacked where the manifest says. A test that
/// takes the native engine library from here runs on what a node is sent, and notices a package
/// that would ship without it. The archives are kept under the tests' <c>@Data/.assets</c>
/// (gitignored) and checked against the manifest on every use.
/// </summary>
internal static class SmtPackagedAssets
{
    #region Constants

    private const string MANIFEST = "controller.json";
    private const string RELEASE_SCHEME = "gh-release://";
    private const string ANY_PLATFORM = "any";
    private const string CACHE_FOLDER = ".assets";
    private const string ASSET_ITEM = "ControllerDataAsset";
    private const int FETCH_ATTEMPTS = 3;

    private static readonly TimeSpan FETCH_TIMEOUT = TimeSpan.FromMinutes(10);

    #endregion

    #region Functions

    /// <summary>The assets a module's manifest declares, in its order.</summary>
    /// <param name="module">The module's folder (its manifest is read).</param>
    /// <returns>The assets; empty when it declares none.</returns>
    public static List<SmtPackagedAsset> Declared(string module)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(module, MANIFEST)));

        var assets = new List<SmtPackagedAsset>();
        if (!document.RootElement.TryGetProperty("dataAssets", out var declared) || declared.ValueKind != JsonValueKind.Array)
            return assets;

        foreach (var item in declared.EnumerateArray())
        {
            assets.Add(new SmtPackagedAsset(
                item.GetProperty("id").GetString() ?? "",
                item.TryGetProperty("runtimeIdentifier", out var rid) ? rid.GetString() ?? ANY_PLATFORM : ANY_PLATFORM,
                item.GetProperty("uri").GetString() ?? "",
                item.GetProperty("sha256").GetString() ?? "",
                item.GetProperty("size").GetInt64(),
                item.GetProperty("extractTo").GetString() ?? ""));
        }

        return assets;
    }

    /// <summary>The assets the controller's project declares.</summary>
    /// <param name="projectFile">The project file.</param>
    /// <returns>The assets, in the project's order.</returns>
    public static List<SmtPackagedAsset> Project(string projectFile)
    {
        return XDocument.Load(projectFile).Descendants(ASSET_ITEM)
            .Select(item => new SmtPackagedAsset(
                (string?)item.Attribute("Include") ?? "",
                (string?)item.Element("RuntimeIdentifier") ?? ANY_PLATFORM,
                (string?)item.Element("Uri") ?? "",
                (string?)item.Element("Sha256") ?? "",
                long.Parse((string?)item.Element("Size") ?? "0"),
                (string?)item.Element("ExtractTo") ?? ""))
            .ToList();
    }

    /// <summary>
    /// Fetches an asset's archive (once: it is kept and checked again on every use), holds it to
    /// the manifest's size and hash, and unpacks it into the module where the manifest says.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="module">The module's folder.</param>
    /// <param name="cache">The folder the archives are kept in.</param>
    /// <returns>The folder the asset was unpacked into.</returns>
    /// <exception cref="InvalidDataException">The archive is not the one the manifest states.</exception>
    /// <exception cref="HttpRequestException">The archive could not be fetched.</exception>
    public static string Resolve(SmtPackagedAsset asset, string module, string cache)
    {
        var archive = Path.Combine(cache, asset.Sha256 + "-" + Path.GetFileName(new Uri(Url(asset)).LocalPath));
        if (!File.Exists(archive) || !Matches(archive, asset))
            Fetch(asset, archive);

        if (!Matches(archive, asset))
        {
            File.Delete(archive);
            throw new InvalidDataException($"the archive of asset '{asset.Id}' is not the one the manifest states (size {asset.Size}, SHA-256 {asset.Sha256})");
        }

        var target = Path.GetFullPath(Path.Combine(module, asset.ExtractTo));
        Directory.CreateDirectory(target);

        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/'))
                continue;

            var path = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!path.StartsWith(target, StringComparison.Ordinal))
                throw new InvalidDataException($"the archive of asset '{asset.Id}' holds a path outside its folder");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }

        return target;
    }

    /// <summary>The folder the archives are kept in, beside the tests' other downloaded data.</summary>
    /// <param name="solutionRoot">The solution root.</param>
    /// <returns>The folder.</returns>
    public static string CacheFolder(string solutionRoot)
    {
        return Path.Combine(solutionRoot, "AI", "OutWit.Controller.AI.Smt.Tests", "@Data", CACHE_FOLDER);
    }

    /// <summary>The address an asset's archive is fetched from: the release of the repository its URI names.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The address.</returns>
    /// <exception cref="InvalidDataException">The URI is not a release asset's.</exception>
    public static string Url(SmtPackagedAsset asset)
    {
        // gh-release://<owner>/<repository>/<tag>/<file>
        var parts = asset.Uri.StartsWith(RELEASE_SCHEME, StringComparison.Ordinal) ? asset.Uri[RELEASE_SCHEME.Length..].Split('/') : [];
        if (parts.Length != 4 || parts.Any(part => part.Length == 0))
            throw new InvalidDataException($"asset '{asset.Id}' has a URI that is not a release asset's");

        return $"https://github.com/{parts[0]}/{parts[1]}/releases/download/{parts[2]}/{parts[3]}";
    }

    private static void Fetch(SmtPackagedAsset asset, string archive)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        var part = archive + ".part";

        using var client = new HttpClient { Timeout = FETCH_TIMEOUT };
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using (var response = client.GetAsync(Url(asset), HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    using var file = File.Create(part);
                    response.Content.CopyToAsync(file).GetAwaiter().GetResult();
                }

                File.Move(part, archive, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && attempt < FETCH_ATTEMPTS)
            {
                Thread.Sleep(TimeSpan.FromSeconds(5 * attempt));
            }
        }
    }

    private static bool Matches(string archive, SmtPackagedAsset asset)
    {
        if (new FileInfo(archive).Length != asset.Size)
            return false;

        using var stream = File.OpenRead(archive);
        return string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
