// Inline MSBuild task of the controller build (compiled by RoslynCodeTaskFactory from the
// UsingTask declaration in OutWit.Controller.Staging.targets). Keep this file inside what
// netstandard2.0 offers: no newer language features, no System.Text.Json.
//
// A module folder ($(SolutionDir)@Controllers/<configuration>/<name>.module/) is written by
// several projects (the controller, its Model, a Model of another controller) and is also
// where people put things by hand: a copy of Blender, a runtime, a file to try. So the build
// keeps its own account of the folder and trusts nothing else:
//
//   - every project that stages files records what its build staged, in a file of its own
//     outside the module folders (@Controllers/.staged/<configuration>/<project>.txt);
//   - the next build of that project removes from the modules only what its own record lists
//     and it no longer stages, and only while the file still has the bytes that build put
//     there. A file no record lists, or one somebody has put other bytes into, is never
//     touched: the build cannot show that it wrote it. One file the build wrote is never
//     removed this way either: a controller's controller.json is recorded before the stamp
//     (ControllerStampManifest) rewrites it in the module, so the recorded bytes are never
//     the ones in the folder; when a project stops staging into a module, that module's
//     controller.json stays, and is reported as a file that is not the build's;
//   - a module is, for everything that leaves the machine (the zip, the package, the content
//     digest), the files the records list, not the files the folder holds.
//
// A record is a text file: comment lines (#), then one staged file a line: its path relative
// to the folder of the modules (forward slashes), then, after tabs, the SHA-256 it was staged
// with and the length and write time it had then (the two let the next build take the hash
// over instead of reading an unchanged file again).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OutWit.Controller.Build
{
    /// <summary>
    /// Records what a project staged into controller modules, removes what its last build staged
    /// and this one does not, and, for a controller, returns the files its module is made of.
    /// </summary>
    public class RecordControllerStagedFiles : Microsoft.Build.Utilities.Task
    {
        #region Constants

        private const string RECORD_EXTENSION = ".txt";
        private const string COMMENT = "#";
        private const string PROJECT_LINE = "# project: ";
        private const string HEADER = "# The files this project staged into controller modules in its last build, relative to the folder of the modules,\n" +
                                      "# each with the SHA-256, the length and the write time it was staged with.\n" +
                                      "# Written by the build (Build/OutWit.Controller.Staging.targets); not to be edited. The next build removes from a module\n" +
                                      "# only what is listed here, is no longer staged and still has these bytes; a module's zip and package hold what these lists name.";
        private const char FIELD = '\t';
        private const int ATTEMPTS = 5;
        private const int ATTEMPT_PAUSE_MS = 100;

        private static readonly StringComparer NAMES = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        #endregion

        #region Task

        public override bool Execute()
        {
            try
            {
                var root = Folder(ModulesRoot);
                var recordsFolder = Folder(RecordsDirectory);
                var ownRecord = Path.Combine(recordsFolder, Path.GetFileNameWithoutExtension(Project) + RECORD_EXTENSION);

                // What this build staged.
                var staged = new SortedSet<string>(StringComparer.Ordinal);
                var stagedNames = new HashSet<string>(NAMES);
                foreach (var item in Staged ?? new ITaskItem[0])
                {
                    var relative = Relative(root, item.ItemSpec);
                    if (relative == null)
                    {
                        Log.LogError("Staged files: {0} is not under the folder of the modules ({1}). A project stages into a module folder and nowhere else.", item.ItemSpec, root);
                        return false;
                    }

                    if (stagedNames.Add(relative))
                        staged.Add(relative);
                }

                // What the last build of this project staged, and what the other projects stage.
                var previous = Read(ownRecord);
                if (previous.Project.Length > 0 && !NAMES.Equals(previous.Project, Path.GetFullPath(Project)) && File.Exists(previous.Project))
                {
                    Log.LogError("Staged files: the record {0} belongs to {1}. Two projects of one name cannot both stage into controller modules: each would remove the other's files as its own.", ownRecord, previous.Project);
                    return false;
                }

                var others = new Dictionary<string, string>(NAMES);
                if (Directory.Exists(recordsFolder))
                {
                    foreach (var path in Directory.GetFiles(recordsFolder, "*" + RECORD_EXTENSION))
                    {
                        if (NAMES.Equals(Path.GetFullPath(path), Path.GetFullPath(ownRecord)))
                            continue;

                        var record = Read(path);
                        if (record.Project.Length == 0 || !File.Exists(record.Project))
                        {
                            // The project is gone: what it once staged is nobody's now. It stays on disk and does not ship.
                            Log.LogMessage(MessageImportance.Low, "Staged files: the record {0} names a project that is not there; it is not counted.", path);
                            continue;
                        }

                        foreach (var file in record.Files)
                        {
                            if (!others.ContainsKey(file))
                                others[file] = record.Project;
                        }
                    }
                }

                // Remove what the last build of this project staged and nobody stages now, while it
                // is still the file that build put there. Nothing else is ever removed: a file no
                // record lists was not put there by a build that can be named, and one with other
                // bytes than the record's was put there by somebody since.
                var removed = new List<ITaskItem>();
                foreach (var file in previous.Files)
                {
                    if (stagedNames.Contains(file) || others.ContainsKey(file))
                        continue;

                    var full = Full(root, file);
                    if (full == null)
                    {
                        Log.LogWarning("Staged files: the record {0} lists '{1}', which is not a path inside the folder of the modules; it is left alone.", ownRecord, file);
                        continue;
                    }

                    if (!File.Exists(full))
                        continue;

                    Mark mark;
                    if (!previous.Marks.TryGetValue(file, out mark) || mark.Sha256.Length == 0 || !string.Equals(mark.Sha256, Sha256(full), StringComparison.Ordinal))
                    {
                        Log.LogMessage(MessageImportance.High, "Staged files: left {0} alone. {1} no longer stages it, and it is not the file its earlier build put there (other bytes): somebody's own. It does not ship.", file, Path.GetFileName(Project));
                        continue;
                    }

                    try
                    {
                        File.SetAttributes(full, FileAttributes.Normal);
                        File.Delete(full);
                        removed.Add(new TaskItem(full));
                        Log.LogMessage(MessageImportance.High, "Staged files: removed {0} (an earlier build of {1} staged it, this one does not).", file, Path.GetFileName(Project));
                    }
                    catch (IOException exception)
                    {
                        Log.LogWarning("Staged files: {0} could not be removed ({1}). It is no longer listed, so it does not ship.", full, exception.Message);
                    }
                    catch (UnauthorizedAccessException exception)
                    {
                        Log.LogWarning("Staged files: {0} could not be removed ({1}). It is no longer listed, so it does not ship.", full, exception.Message);
                    }
                }

                Removed = removed.ToArray();
                Write(ownRecord, staged, root, previous);

                if (string.IsNullOrEmpty(ModuleDirectory))
                    return !Log.HasLoggedErrors;

                return Module(root, staged, stagedNames, others);
            }
            catch (Exception exception)
            {
                Log.LogErrorFromException(exception, true);
                return false;
            }
        }

        #endregion

        #region Module

        /// <summary>The files a controller's module is made of: what the records list under its folder.</summary>
        private bool Module(string root, SortedSet<string> staged, HashSet<string> stagedNames, Dictionary<string, string> others)
        {
            var module = Relative(root, Path.Combine(ModuleDirectory, "x"));
            if (module == null || module.IndexOf('/') != module.LastIndexOf('/'))
            {
                Log.LogError("Staged files: the module folder {0} is not a folder directly under the folder of the modules ({1}).", ModuleDirectory, root);
                return false;
            }

            var prefix = module.Substring(0, module.Length - 1);
            var listed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in staged)
            {
                if (file.StartsWith(prefix, StringComparison.Ordinal))
                    listed[file] = Project;
            }

            foreach (var other in others)
            {
                if (other.Key.StartsWith(prefix, NAMES == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) && !stagedNames.Contains(other.Key))
                    listed[other.Key] = other.Value;
            }

            var excluded = Folders(PackExcludedFolders);
            var names = new HashSet<string>(NAMES);
            var moduleFiles = new List<ITaskItem>();
            var packedFiles = new List<ITaskItem>();
            foreach (var file in listed)
            {
                var inside = file.Key.Substring(prefix.Length);
                var full = Full(root, file.Key);
                if (full == null || !File.Exists(full))
                {
                    Log.LogError("Staged files: {0} lists {1} as staged by its last build, and the file is not in the module. Build that project again (a build that builds its dependencies does it).", Path.GetFileName(file.Value), file.Key);
                    continue;
                }

                names.Add(inside);
                moduleFiles.Add(Item(full, inside));
                if (!Under(inside, excluded))
                    packedFiles.Add(Item(full, inside));
            }

            // What the controller's declared data assets extract to, when a copy of it lies in
            // the folder: kept in the zip, as before. A root-extracted asset has no folder of its own.
            var moduleFolder = Folder(ModuleDirectory);
            var assetFiles = new List<ITaskItem>();
            foreach (var folder in Folders(DataAssetFolders))
            {
                var directory = Full(moduleFolder, folder.TrimEnd('/'));
                if (directory == null)
                {
                    Log.LogError("Staged files: the data asset folder '{0}' is not a folder inside the module.", folder);
                    continue;
                }

                if (!Directory.Exists(directory))
                    continue;

                foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    var inside = Relative(moduleFolder, path);
                    if (inside != null && names.Add(inside))
                        assetFiles.Add(Item(path, inside));
                }
            }

            // An assembly of a project this controller references lies in the module and no record
            // lists it: its project was not built since records are kept (a build without
            // dependencies), and the module would ship without it.
            foreach (var reference in ProjectReferenceAssemblies ?? new ITaskItem[0])
            {
                var name = Path.GetFileName(reference.ItemSpec);
                if (name.Length > 0 && File.Exists(Path.Combine(moduleFolder, name)) && !names.Contains(name))
                    Log.LogError("Staged files: {0} lies in {1} and no project's record lists it, so the module would ship without it. Build its project (a build that builds its dependencies does it).", name, ModuleDirectory);
            }

            ModuleFiles = moduleFiles.ToArray();
            PackedFiles = packedFiles.ToArray();
            DataAssetFiles = assetFiles.ToArray();
            return !Log.HasLoggedErrors;
        }

        #endregion

        #region Tools

        private static ITaskItem Item(string path, string inside)
        {
            var item = new TaskItem(path);
            item.SetMetadata("ModulePath", inside);
            return item;
        }

        /// <summary>A record's project and files; a record that is not there has neither.</summary>
        private static Record Read(string path)
        {
            var record = new Record();
            if (!File.Exists(path))
                return record;

            foreach (var raw in Retry(() => File.ReadAllLines(path, Encoding.UTF8)))
            {
                var line = raw.Trim();
                if (line.StartsWith(PROJECT_LINE, StringComparison.Ordinal))
                {
                    record.Project = line.Substring(PROJECT_LINE.Length).Trim();
                    continue;
                }

                if (line.Length == 0 || line.StartsWith(COMMENT, StringComparison.Ordinal))
                    continue;

                var fields = line.Split(FIELD);
                var mark = new Mark();
                if (fields.Length >= 4)
                {
                    mark.Sha256 = fields[1];
                    long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out mark.Length);
                    long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out mark.WriteTicks);
                }

                record.Files.Add(fields[0]);
                record.Marks[fields[0]] = mark;
            }

            return record;
        }

        /// <summary>
        /// Writes a record so that a reader sees the old one or the new one, never a part. Each
        /// file is listed with the bytes it has now, just staged: the hash is taken over from the
        /// last record when the file's length and write time are the ones recorded there.
        /// </summary>
        private void Write(string path, SortedSet<string> files, string root, Record previous)
        {
            var text = new StringBuilder();
            text.Append(HEADER).Append('\n');
            text.Append(PROJECT_LINE).Append(Path.GetFullPath(Project)).Append('\n');
            foreach (var file in files)
            {
                var mark = new Mark();
                var full = Full(root, file);
                if (full != null && File.Exists(full))
                {
                    var info = new FileInfo(full);
                    mark.Length = info.Length;
                    mark.WriteTicks = info.LastWriteTimeUtc.Ticks;

                    Mark last;
                    mark.Sha256 = previous.Marks.TryGetValue(file, out last) && last.Sha256.Length > 0 && last.Length == mark.Length && last.WriteTicks == mark.WriteTicks
                        ? last.Sha256
                        : Sha256(full);
                }

                text.Append(file).Append(FIELD).Append(mark.Sha256).Append(FIELD)
                    .Append(mark.Length.ToString(CultureInfo.InvariantCulture)).Append(FIELD)
                    .Append(mark.WriteTicks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            var bytes = new UTF8Encoding(false).GetBytes(text.ToString());
            if (File.Exists(path) && Same(Retry(() => File.ReadAllBytes(path)), bytes))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            Retry(() =>
            {
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);

                return true;
            });
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = Retry(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                var builder = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(stream))
                    builder.Append(b.ToString("x2"));

                return builder.ToString();
            }
        }

        private static bool Same(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static T Retry<T>(Func<T> action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return action();
                }
                catch (IOException)
                {
                    if (attempt == ATTEMPTS)
                        throw;

                    System.Threading.Thread.Sleep(ATTEMPT_PAUSE_MS * attempt);
                }
            }
        }

        /// <summary>
        /// A path as this system writes it. The build files write paths with backslashes; where
        /// the separator is a slash MSBuild turns most of them, and this turns the rest.
        /// </summary>
        private static string Native(string path)
        {
            return Path.DirectorySeparatorChar == '/' ? path.Replace('\\', '/') : path;
        }

        /// <summary>A folder's full path with one separator at its end.</summary>
        private static string Folder(string path)
        {
            return Path.GetFullPath(Native(path)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        /// <summary>A path relative to a folder, with forward slashes; null when it is not inside the folder.</summary>
        private static string Relative(string folder, string path)
        {
            var full = Path.GetFullPath(Native(path));
            if (!full.StartsWith(folder, NAMES == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) || full.Length == folder.Length)
                return null;

            return full.Substring(folder.Length).Replace('\\', '/');
        }

        /// <summary>The full path of a relative name inside a folder; null when the name leaves the folder.</summary>
        private static string Full(string folder, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                return null;

            foreach (var part in relative.Split('/', '\\'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                    return null;
            }

            return Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>Folder names of a module as prefixes ("tools/win-x64/"); the module's own root (".", empty) is not one.</summary>
        private static List<string> Folders(ITaskItem[] items)
        {
            var folders = new List<string>();
            foreach (var item in items ?? new ITaskItem[0])
            {
                var folder = (item.ItemSpec ?? "").Trim().Replace('\\', '/');
                while (folder.StartsWith("./", StringComparison.Ordinal))
                    folder = folder.Substring(2);

                folder = folder.Trim('/');
                if (folder.Length == 0 || folder == ".")
                    continue;

                if (!folders.Contains(folder + "/"))
                    folders.Add(folder + "/");
            }

            return folders;
        }

        private static bool Under(string inside, List<string> folders)
        {
            foreach (var folder in folders)
            {
                if (inside.StartsWith(folder, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        #endregion

        #region Properties

        /// <summary>The files this build of the project staged: full paths inside module folders.</summary>
        public ITaskItem[] Staged { get; set; }

        /// <summary>The folder of the modules of this configuration (@Controllers/&lt;configuration&gt;/).</summary>
        [Required]
        public string ModulesRoot { get; set; }

        /// <summary>The folder of the records of this configuration, outside the module folders.</summary>
        [Required]
        public string RecordsDirectory { get; set; }

        /// <summary>The project that stages: its record is named after it.</summary>
        [Required]
        public string Project { get; set; }

        /// <summary>A controller's own module folder. When given, the files the module is made of are returned.</summary>
        public string ModuleDirectory { get; set; }

        /// <summary>The folders the controller's data assets extract to, relative to the module (a "." is the module itself and counts for nothing).</summary>
        public ITaskItem[] DataAssetFolders { get; set; }

        /// <summary>Folders of the module the package leaves out (Resources).</summary>
        public ITaskItem[] PackExcludedFolders { get; set; }

        /// <summary>The assemblies of the projects the controller references, to notice one that lies in the module unrecorded.</summary>
        public ITaskItem[] ProjectReferenceAssemblies { get; set; }

        /// <summary>The files removed: what the project's last build staged and nobody stages now.</summary>
        [Output]
        public ITaskItem[] Removed { get; set; }

        /// <summary>The module's files by the records of every project (metadata ModulePath: the path inside the module, forward slashes).</summary>
        [Output]
        public ITaskItem[] ModuleFiles { get; set; }

        /// <summary>The module's files the package ships: <see cref="ModuleFiles"/> without the excluded folders.</summary>
        [Output]
        public ITaskItem[] PackedFiles { get; set; }

        /// <summary>Files that lie under a declared data asset folder and that no record lists.</summary>
        [Output]
        public ITaskItem[] DataAssetFiles { get; set; }

        #endregion

        #region Record

        /// <summary>What a record file says.</summary>
        private sealed class Record
        {
            #region Fields

            public string Project = "";

            public readonly List<string> Files = new List<string>();

            public readonly Dictionary<string, Mark> Marks = new Dictionary<string, Mark>(NAMES);

            #endregion
        }

        #endregion

        #region Mark

        /// <summary>What a staged file was when it was recorded: its bytes by their hash, its length and its write time.</summary>
        private sealed class Mark
        {
            #region Fields

            public string Sha256 = "";

            public long Length;

            public long WriteTicks;

            #endregion
        }

        #endregion
    }
}
