using System.IO.Compression;
using OutWit.Controller.Pack.Options;
using OutWit.Engine.Assets.Manifest;

namespace OutWit.Controller.Pack
{
    public static class PackRunner
    {
        #region Constants

        public const string MANIFEST_FILENAME = "controller.json";

        #endregion

        #region Methods

        public static Task RunAsync(PackOptions options)
        {
            ValidateModuleDir(options.ModuleDir);

            var manifestPath = Path.Combine(options.ModuleDir, MANIFEST_FILENAME);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException(
                    $"No {MANIFEST_FILENAME} found in module directory '{options.ModuleDir}'. " +
                    $"Build the controller first so its manifest is emitted.",
                    manifestPath);

            var manifest = AssetManifestReader.ReadFromFile(manifestPath);
            Console.WriteLine($"Controller: {manifest.Name} {manifest.Version}");
            Console.WriteLine($"  module dir: {options.ModuleDir}");
            Console.WriteLine($"  data assets in manifest: {manifest.DataAssets.Count}");

            ValidateAssetUris(manifest, options.AllowExternalUris, options.ModuleDir);

            var outputPath = ResolveOutputPath(options.OutputPath, manifest);
            Console.WriteLine($"  output zip: {outputPath}");

            CreateZip(options.ModuleDir, outputPath);

            var info = new FileInfo(outputPath);
            Console.WriteLine();
            Console.WriteLine($"Packed: {outputPath} ({FormatSize(info.Length)})");
            Console.WriteLine($"Upload via WitCloud admin UI -> Controllers -> Upload.");
            return Task.CompletedTask;
        }

        #endregion

        #region Tools

        private static void ValidateModuleDir(string moduleDir)
        {
            if (string.IsNullOrWhiteSpace(moduleDir))
                throw new ArgumentException("Module directory is empty.", nameof(moduleDir));
            if (!Directory.Exists(moduleDir))
                throw new DirectoryNotFoundException($"Module directory not found: '{moduleDir}'.");
        }

        private static void ValidateAssetUris(AssetManifest manifest, bool allowExternal, string moduleDir)
        {
            var external = 0;
            var inline = 0;
            var missing = 0;

            foreach (var entry in manifest.DataAssets)
            {
                var scheme = entry.Source.Scheme;
                if (string.Equals(scheme, Uri.UriSchemeFile, StringComparison.Ordinal))
                {
                    inline++;
                    var localPath = ResolveFileUriRelativeTo(entry.Source, moduleDir);
                    if (!File.Exists(localPath))
                    {
                        Console.Error.WriteLine(
                            $"  warning: asset '{entry.Id}' references missing file '{localPath}'.");
                        missing++;
                    }
                }
                else
                {
                    external++;
                    if (!allowExternal)
                    {
                        throw new InvalidOperationException(
                            $"Asset '{entry.Id}' has non-file URI '{entry.Source}'. " +
                            $"Contributor zips bundle assets inline by default — use --allow-external-uris " +
                            $"to opt in to external URIs (requires server-side policy to also permit them).");
                    }
                }
            }

            Console.WriteLine($"  asset breakdown: {inline} inline, {external} external{(missing > 0 ? $", {missing} missing files" : "")}");

            if (missing > 0)
            {
                throw new InvalidOperationException(
                    $"{missing} declared asset file(s) not found relative to the module directory. " +
                    $"Place them inside the module before packing.");
            }
        }

        private static string ResolveFileUriRelativeTo(Uri uri, string baseDir)
        {
            if (uri.IsAbsoluteUri && uri.Host is not (null or "" or "."))
            {
                var relative = uri.Host + uri.AbsolutePath.TrimStart('/');
                return Path.GetFullPath(Path.Combine(baseDir, relative));
            }
            if (uri.IsAbsoluteUri && (uri.Host == "" || uri.Host == "."))
            {
                var path = uri.AbsolutePath.TrimStart('/');
                if (Path.IsPathRooted(path) || string.IsNullOrEmpty(uri.Host))
                    return uri.LocalPath;
                return Path.GetFullPath(Path.Combine(baseDir, path));
            }
            return uri.LocalPath;
        }

        private static string ResolveOutputPath(string? optionsOutput, AssetManifest manifest)
        {
            if (!string.IsNullOrWhiteSpace(optionsOutput))
                return Path.GetFullPath(optionsOutput);
            var defaultName = $"{manifest.Name}-{manifest.Version}.zip";
            return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, defaultName));
        }

        private static void CreateZip(string moduleDir, string outputPath)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);
            ZipFile.CreateFromDirectory(moduleDir, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion
    }
}
