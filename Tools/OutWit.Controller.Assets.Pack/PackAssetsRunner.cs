using OutWit.Controller.Assets.Pack.Csproj;
using OutWit.Controller.Assets.Pack.Options;
using OutWit.Controller.Assets.Pack.Packers;
using OutWit.Controller.Assets.Pack.GitHub;

namespace OutWit.Controller.Assets.Pack
{
    public static class PackAssetsRunner
    {
        #region Constants

        private const string DefaultOutputSubdir = "dist";

        #endregion

        #region Methods

        public static async Task<int> RunAsync(PackAssetsOptions options, CancellationToken ct = default)
        {
            var resolved = ResolvedOptions.From(options);

            Console.WriteLine($"outwit-assets-pack");
            Console.WriteLine($"  csproj         : {resolved.CsprojPath}");
            Console.WriteLine($"  prerequisites  : {resolved.PrerequisitesRoot}");
            Console.WriteLine($"  output         : {resolved.OutputDir}");
            Console.WriteLine($"  apply          : {options.Apply}");
            Console.WriteLine($"  push-release   : {options.PushRelease}");
            Console.WriteLine();

            var editor = ControllerDataAssetEditor.Load(resolved.CsprojPath);
            var newVersion = options.Version ?? editor.Version;
            var releaseTag = options.ReleaseTag ?? DefaultReleaseTag(editor.ControllerName, newVersion);

            Console.WriteLine($"  controller     : {editor.ControllerName}");
            Console.WriteLine($"  version        : {editor.Version}  ->  {newVersion}");
            Console.WriteLine($"  release tag    : {releaseTag}");
            Console.WriteLine($"  data assets    : {editor.Entries.Count}");
            Console.WriteLine();

            if (editor.Entries.Count == 0)
            {
                Console.Error.WriteLine($"Csproj '{resolved.CsprojPath}' has no <ControllerDataAsset> items — nothing to pack.");
                return 1;
            }

            if (options.Clean && Directory.Exists(resolved.OutputDir))
            {
                Console.WriteLine($"Cleaning output dir: {resolved.OutputDir}");
                Directory.Delete(resolved.OutputDir, recursive: true);
            }

            // ---- Pack each asset & gather results -----------------------------------
            var packer = new AssetPacker();
            var updates = new Dictionary<string, AssetUpdate>(StringComparer.Ordinal);
            var packResults = new List<PackResult>(editor.Entries.Count);

            foreach (var entry in editor.Entries)
            {
                var result = packer.Pack(entry, resolved.PrerequisitesRoot, resolved.OutputDir, forceClean: false);
                Console.WriteLine($"  {(result.Reused ? "reuse" : "pack ")} {result.ArtifactName}");

                var newUri = options.Version is null && options.ReleaseTag is null
                    ? entry.Uri
                    : RewriteUriToTag(entry.Uri, releaseTag);

                updates[entry.Id] = new AssetUpdate(result.Sha256, result.Size, newUri);
                packResults.Add(result);
            }

            // ---- Print diff vs current csproj ---------------------------------------
            Console.WriteLine();
            Console.WriteLine("Diff vs current csproj:");
            Console.WriteLine();
            Console.WriteLine($"  {"Asset",-25} {"Old SHA",-10} {"New SHA",-10} {"Old Size",-14} {"New Size",-14} {"Status",-10}");
            Console.WriteLine("  " + new string('-', 90));

            var dirty = !string.Equals(editor.Version, newVersion, StringComparison.Ordinal);
            foreach (var entry in editor.Entries)
            {
                var u = updates[entry.Id];
                var oldSha8 = Truncate(entry.Sha256, 8);
                var newSha8 = Truncate(u.Sha256, 8);
                var changed = !string.Equals(entry.Sha256, u.Sha256, StringComparison.Ordinal)
                              || entry.Size != u.Size
                              || !string.Equals(entry.Uri, u.Uri, StringComparison.Ordinal);
                if (changed) dirty = true;
                Console.WriteLine($"  {entry.Id,-25} {oldSha8,-10} {newSha8,-10} {entry.Size,-14:N0} {u.Size,-14:N0} {(changed ? "CHANGED" : "unchanged"),-10}");
            }

            // ---- Apply if requested -------------------------------------------------
            if (options.Apply)
            {
                Console.WriteLine();
                if (dirty)
                {
                    var newText = editor.Apply(updates, options.Version is null && options.ReleaseTag is null ? null : newVersion);
                    editor.Save(newText);
                    Console.WriteLine($"  wrote: {resolved.CsprojPath}");
                }
                else
                {
                    Console.WriteLine($"  csproj already in sync — nothing to write.");
                }
            }

            // ---- Push to GitHub Release if requested --------------------------------
            if (options.PushRelease)
            {
                Console.WriteLine();
                Console.WriteLine($"Pushing release '{releaseTag}' to {options.Owner}/{options.Repo}...");

                var token = ResolveToken(options);
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                var client = new GitHubReleaseClient(http, options.Owner, options.Repo, token);

                var assetTuples = packResults.Select(r => (r.ArtifactName, r.ArtifactPath));
                var releaseInfo = await client.EnsureReleaseAsync(
                    tag: releaseTag,
                    name: $"OutWit.Controller.{editor.ControllerName} v{newVersion}",
                    body: $"Tier-2 assets for OutWit.Controller.{editor.ControllerName} {newVersion}.",
                    assets: assetTuples,
                    log: new Progress<string>(msg => Console.WriteLine($"  {msg}")),
                    ct: ct);

                Console.WriteLine();
                Console.WriteLine($"Release ready: https://github.com/{options.Owner}/{options.Repo}/releases/tag/{releaseTag}");
                Console.WriteLine($"  assets on release: {releaseInfo.Assets.Count}");
            }

            // ---- Next-step hints (only without -PushRelease) ------------------------
            if (!options.PushRelease)
            {
                Console.WriteLine();
                Console.WriteLine("Next steps:");
                if (!options.Apply)
                    Console.WriteLine($"  1) Re-run with --apply to update {Path.GetFileName(resolved.CsprojPath)}.");
                Console.WriteLine($"  {(options.Apply ? "1" : "2")}) Re-run with --push-release (and a token) to upload all artifacts to GitHub Release '{releaseTag}'.");
            }

            return 0;
        }

        #endregion

        #region Tools

        private static string DefaultReleaseTag(string controllerName, string version)
        {
            return $"{controllerName.ToLowerInvariant()}-v{version}";
        }

        private static string RewriteUriToTag(string oldUri, string newTag)
        {
            // gh-release://owner/repo/<oldTag>/filename.ext -> gh-release://owner/repo/<newTag>/filename.ext
            // Replace ONLY the tag segment. Defensive: only rewrite gh-release:// URIs.
            if (!oldUri.StartsWith("gh-release://", StringComparison.Ordinal))
                return oldUri;

            // Split into 'gh-release://owner/repo/' + tag + '/filename.ext'
            var prefix = "gh-release://";
            var rest = oldUri[prefix.Length..];
            var segments = rest.Split('/');
            if (segments.Length < 4)
                return oldUri;  // unexpected shape — don't break it
            segments[2] = newTag;
            return prefix + string.Join("/", segments);
        }

        private static string ResolveToken(PackAssetsOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.Token))
                return options.Token;

            var envName = string.IsNullOrWhiteSpace(options.TokenEnv) ? "GH_PACKAGE_TOKEN" : options.TokenEnv;
            var fromEnv = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return fromEnv;

            throw new InvalidOperationException(
                $"GitHub token not provided. Set --token <value> or define ${envName} in the environment. " +
                $"The token needs 'contents:write' on the target repo.");
        }

        private static string Truncate(string s, int len)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= len ? s : s[..len];
        }

        #endregion

        #region Resolved options

        private sealed class ResolvedOptions
        {
            public required string CsprojPath { get; init; }

            public required string PrerequisitesRoot { get; init; }

            public required string OutputDir { get; init; }

            public static ResolvedOptions From(PackAssetsOptions raw)
            {
                if (string.IsNullOrWhiteSpace(raw.CsprojPath))
                    throw new ArgumentException("csproj path is required.", nameof(raw));
                if (string.IsNullOrWhiteSpace(raw.Prerequisites))
                    throw new ArgumentException("--prerequisites is required.", nameof(raw));

                var csproj = Path.GetFullPath(raw.CsprojPath);
                if (!File.Exists(csproj))
                    throw new FileNotFoundException($"Csproj not found: '{csproj}'.", csproj);

                var prereqs = Path.GetFullPath(raw.Prerequisites);
                if (!Directory.Exists(prereqs))
                    throw new DirectoryNotFoundException($"Prerequisites root not found: '{prereqs}'.");

                var outputDir = raw.Output is { Length: > 0 }
                    ? Path.GetFullPath(raw.Output)
                    : DefaultOutputDir(csproj);

                return new ResolvedOptions
                {
                    CsprojPath = csproj,
                    PrerequisitesRoot = prereqs,
                    OutputDir = outputDir,
                };
            }

            private static string DefaultOutputDir(string csprojPath)
            {
                // <csproj-dir>/../.. is typically the repo root (Render/<csproj-dir>/<csproj>),
                // and we drop artifacts in <repo>/dist/. Sub-folder per controller keeps
                // multiple controllers' staged artifacts from colliding.
                var csprojDir = Path.GetDirectoryName(csprojPath)!;
                var repoRoot = Path.GetFullPath(Path.Combine(csprojDir, "..", ".."));
                var controllerNameSegment = Path.GetFileNameWithoutExtension(csprojPath).ToLowerInvariant();
                return Path.Combine(repoRoot, DefaultOutputSubdir, controllerNameSegment);
            }
        }

        #endregion
    }
}
