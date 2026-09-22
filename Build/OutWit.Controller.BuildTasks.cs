// Inline MSBuild tasks of the controller build (compiled by RoslynCodeTaskFactory from the
// UsingTask declarations in OutWit.Controller.Manifest.targets). Keep this file inside what
// netstandard2.0 offers: no newer language features, no System.Text.Json.
//
// Two tasks:
//   StampControllerManifest  - computes the content digest of the staged module and writes it,
//                              with the build stamp, into the module's controller.json.
//   DeterministicZipDirectory - zips the module with sorted entries and a fixed timestamp, so
//                              the same content gives the same bytes on every machine.
//
// The content digest is the identity of a controller build that WitCloud compares (its
// ControllerContentDigest class implements the same definition; the two must not drift):
//   every file of the module except controller.json (and, at pack time, Resources/**, which the
//   nupkg leaves out), ordered by relative path (forward slashes, ordinal, case-sensitive);
//   SHA-256 over, per file, UTF-8 path + LF + the 32 raw bytes of the file's SHA-256 + LF;
//   written as "sha256:" + lower-case hex.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OutWit.Controller.Build
{
    public static class ControllerBuildDigest
    {
        public const string PREFIX = "sha256:";
        public const string MANIFEST_FILENAME = "controller.json";

        /// <summary>
        /// The content digest of a module folder; files under any of the excluded top-level
        /// folders (relative, forward slashes, e.g. "Resources") are left out.
        /// </summary>
        public static string OfDirectory(string rootDirectory, IEnumerable<string> excludedFolders)
        {
            var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var excluded = new List<string>();
            foreach (var folder in excludedFolders ?? new string[0])
            {
                var trimmed = (folder ?? "").Replace('\\', '/').Trim('/');
                if (trimmed.Length > 0)
                    excluded.Add(trimmed + "/");
            }

            var files = new List<KeyValuePair<string, string>>();
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                if (string.Equals(relative, MANIFEST_FILENAME, StringComparison.Ordinal))
                    continue;

                var skip = false;
                foreach (var prefix in excluded)
                {
                    if (relative.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                    files.Add(new KeyValuePair<string, string>(relative, path));
            }

            files.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

            using (var total = SHA256.Create())
            {
                var newline = new byte[] { 0x0A };
                foreach (var file in files)
                {
                    byte[] fileHash;
                    using (var fileSha = SHA256.Create())
                    using (var stream = File.OpenRead(file.Value))
                        fileHash = fileSha.ComputeHash(stream);

                    var pathBytes = Encoding.UTF8.GetBytes(file.Key);
                    total.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
                    total.TransformBlock(newline, 0, 1, null, 0);
                    total.TransformBlock(fileHash, 0, fileHash.Length, null, 0);
                    total.TransformBlock(newline, 0, 1, null, 0);
                }

                total.TransformFinalBlock(new byte[0], 0, 0);
                var builder = new StringBuilder(PREFIX, PREFIX.Length + 64);
                foreach (var b in total.Hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// Writes the build stamp and the content digest into the module's controller.json by
    /// replacing the placeholders the manifest generator left there.
    /// </summary>
    public class StampControllerManifest : Microsoft.Build.Utilities.Task
    {
        public const string BUILD_PLACEHOLDER = "@CONTROLLER_BUILD@";
        public const string DIGEST_PLACEHOLDER = "@CONTROLLER_CONTENT_DIGEST@";

        [Required]
        public string ModuleDirectory { get; set; }

        [Required]
        public string ManifestFile { get; set; }

        public string Build { get; set; }

        /// <summary>Top-level module folders the digest leaves out (semicolon list), e.g. Resources.</summary>
        public string ExcludedFolders { get; set; }

        [Output]
        public string ContentDigest { get; set; }

        public override bool Execute()
        {
            try
            {
                var excluded = (ExcludedFolders ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                ContentDigest = ControllerBuildDigest.OfDirectory(ModuleDirectory, excluded);

                if (!File.Exists(ManifestFile))
                {
                    Log.LogError("Manifest not found: {0}", ManifestFile);
                    return false;
                }

                var text = File.ReadAllText(ManifestFile, Encoding.UTF8);
                var stamped = text
                    .Replace(BUILD_PLACEHOLDER, JsonEscape(Build ?? ""))
                    .Replace(DIGEST_PLACEHOLDER, ContentDigest);

                if (!string.Equals(text, stamped, StringComparison.Ordinal))
                    File.WriteAllText(ManifestFile, stamped, new UTF8Encoding(false));

                Log.LogMessage(MessageImportance.Normal, "Controller manifest stamped: build '{0}', content digest {1}", Build, ContentDigest);
                return true;
            }
            catch (Exception exception)
            {
                Log.LogErrorFromException(exception, false);
                return false;
            }
        }

        private static string JsonEscape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }

    /// <summary>
    /// A drop-in for ZipDirectory whose output does not depend on the machine: entries sorted
    /// by relative path, forward slashes, a fixed timestamp, no directory entries.
    /// </summary>
    public class DeterministicZipDirectory : Microsoft.Build.Utilities.Task
    {
        public static readonly DateTimeOffset ENTRY_TIME = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        [Required]
        public string SourceDirectory { get; set; }

        [Required]
        public string DestinationFile { get; set; }

        public bool Overwrite { get; set; }

        public override bool Execute()
        {
            try
            {
                var root = Path.GetFullPath(SourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(root))
                {
                    Log.LogError("Source directory not found: {0}", root);
                    return false;
                }

                var destination = Path.GetFullPath(DestinationFile);
                if (File.Exists(destination) && !Overwrite)
                {
                    Log.LogError("Destination file already exists and Overwrite is false: {0}", destination);
                    return false;
                }

                var files = new List<KeyValuePair<string, string>>();
                foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    files.Add(new KeyValuePair<string, string>(relative, path));
                }

                files.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var temporary = destination + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (var file in files)
                    {
                        var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                        entry.LastWriteTime = ENTRY_TIME;
                        using (var input = File.OpenRead(file.Value))
                        using (var output = entry.Open())
                            input.CopyTo(output);
                    }
                }

                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(temporary, destination);

                Log.LogMessage(MessageImportance.Normal, "Packed {0} file(s) from {1} into {2} (deterministic order and timestamps)", files.Count, root, destination);
                return true;
            }
            catch (Exception exception)
            {
                Log.LogErrorFromException(exception, false);
                return false;
            }
        }
    }
}
