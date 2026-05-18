using System.IO.Compression;
using System.Security.Cryptography;
using OutWit.Controller.Assets.Pack.Csproj;

namespace OutWit.Controller.Assets.Pack.Packers
{
    /// <summary>
    /// Stages a single ControllerDataAsset's source bytes into the output dir:
    /// 'zip-folder' kind -> recursive zip of the source directory (no leading
    /// folder entry); 'single-file' kind -> verbatim copy. Computes SHA256
    /// and size of the staged artifact for the csproj rewrite step.
    /// </summary>
    public sealed class AssetPacker
    {
        #region Methods

        /// <summary>
        /// Pack one asset. Reuses an existing artifact at the destination
        /// (only re-hashes it) unless <paramref name="forceClean"/> is set.
        /// </summary>
        /// <param name="entry">Source asset declaration.</param>
        /// <param name="prerequisitesRoot">Author-side root that PackSource is relative to.</param>
        /// <param name="outputDir">Where the artifact lands.</param>
        /// <param name="forceClean">If true, delete an existing artifact before re-packing.</param>
        public PackResult Pack(
            ControllerDataAssetEntry entry,
            string prerequisitesRoot,
            string outputDir,
            bool forceClean = false)
        {
            if (string.IsNullOrWhiteSpace(entry.PackSource))
                throw new InvalidOperationException(
                    $"Asset '{entry.Id}' has no <PackSource>. Author-side metadata is required for packing.");

            var artifactName = ArtifactNameFromUri(entry.Uri, entry.Id);
            var artifactPath = Path.Combine(outputDir, artifactName);
            var sourcePath = Path.Combine(prerequisitesRoot, entry.PackSource);

            if (forceClean && File.Exists(artifactPath))
                File.Delete(artifactPath);

            var produced = false;
            if (!File.Exists(artifactPath))
            {
                Directory.CreateDirectory(outputDir);
                ProduceArtifact(entry.PackKind, sourcePath, artifactPath);
                produced = true;
            }

            var sha = ComputeSha256(artifactPath);
            var size = new FileInfo(artifactPath).Length;

            return new PackResult
            {
                Entry = entry,
                ArtifactName = artifactName,
                ArtifactPath = artifactPath,
                Sha256 = sha,
                Size = size,
                Reused = !produced,
            };
        }

        #endregion

        #region Tools

        private static void ProduceArtifact(string packKind, string sourcePath, string destPath)
        {
            switch (packKind)
            {
                case ControllerDataAssetEntry.PackKindZipFolder:
                    if (!Directory.Exists(sourcePath))
                        throw new DirectoryNotFoundException(
                            $"PackSource directory not found for zip-folder kind: '{sourcePath}'.");
                    ZipFile.CreateFromDirectory(sourcePath, destPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                    return;

                case ControllerDataAssetEntry.PackKindSingleFile:
                    if (!File.Exists(sourcePath))
                        throw new FileNotFoundException(
                            $"PackSource file not found for single-file kind: '{sourcePath}'.", sourcePath);
                    File.Copy(sourcePath, destPath, overwrite: true);
                    return;

                default:
                    throw new InvalidOperationException(
                        $"Unknown PackKind '{packKind}'. Expected '{ControllerDataAssetEntry.PackKindZipFolder}' or '{ControllerDataAssetEntry.PackKindSingleFile}'.");
            }
        }

        /// <summary>
        /// Derives the GitHub Release asset filename from the asset URI.
        /// gh-release://owner/repo/tag/filename.ext -> filename.ext.
        /// </summary>
        public static string ArtifactNameFromUri(string uri, string assetId)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new InvalidOperationException($"Asset '{assetId}' has no <Uri>.");

            var lastSlash = uri.LastIndexOf('/');
            if (lastSlash < 0 || lastSlash == uri.Length - 1)
                throw new InvalidOperationException(
                    $"Cannot derive filename from URI '{uri}' for asset '{assetId}'.");

            return uri[(lastSlash + 1)..];
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha.ComputeHash(stream);
            return Convert.ToHexStringLower(hash);
        }

        #endregion
    }

    /// <summary>
    /// Result of packing one ControllerDataAsset — the staged artifact file
    /// plus its measured Sha256 + Size.
    /// </summary>
    public sealed class PackResult
    {
        public required ControllerDataAssetEntry Entry { get; init; }

        public required string ArtifactName { get; init; }

        public required string ArtifactPath { get; init; }

        public required string Sha256 { get; init; }

        public required long Size { get; init; }

        /// <summary>True when an existing artifact was reused (not re-produced).</summary>
        public bool Reused { get; init; }
    }
}
