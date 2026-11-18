// Inline MSBuild task of the Corpus controller's build (compiled by RoslynCodeTaskFactory from
// the UsingTask declaration in CorpusNotices.targets; not part of the controller's assembly).
// Keep this file inside what netstandard2.0 offers: no newer language features, no
// System.Text.Json.
//
// GenerateCorpusNotices writes the THIRD-PARTY-NOTICES file that travels inside corpus.module
// (and so inside the zip and the package): every library and every piece of data the module
// redistributes, with its version, its licence, where it comes from, which file of the module
// holds it, and the licence text. It refuses to write a notice it cannot stand behind:
//
//   - a package component is checked against the package the build restored: the nuspec in the
//     NuGet cache must state the version, the licence expression and the source commit the
//     component declares, and the assembly staged beside the controller must be, byte for byte,
//     a file of that package;
//   - every licence text is held to the SHA-256 the component declares (over the text with its
//     line ends as line feeds, so a checkout's line ends do not matter);
//   - every assembly the module carries beside the controller's own (ControllerModuleAssembly)
//     must have a component: a new redistributed library without a notice fails the build.
//
// The licence texts are files of the repository, taken once from the commit each package's own
// nuspec names (the packages carry a licence expression, not the text). The output is a
// function of its inputs: fixed order, line feeds, UTF-8 without a byte order mark.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace OutWit.Controller.AI.Corpus.Build
{
    /// <summary>Writes the module's third-party notices and the licence files that travel beside them.</summary>
    public class GenerateCorpusNotices : Microsoft.Build.Utilities.Task
    {
        #region Constants

        private const string KIND_PACKAGE = "package";
        private const string RULE = "==============================================================================";

        #endregion

        #region Task

        public override bool Execute()
        {
            try
            {
                var problems = new List<string>();
                var entries = new List<Entry>();
                foreach (var item in Components ?? new ITaskItem[0])
                    entries.Add(Read(item, problems));

                CheckModuleAssemblies(entries, problems);

                foreach (var problem in problems)
                    Log.LogError("Third-party notices: {0}", problem);
                if (problems.Count > 0)
                    return false;

                WriteIfChanged(OutputFile, Compose(entries));

                foreach (var copy in Copies ?? new ITaskItem[0])
                {
                    var text = ReadText(copy.ItemSpec);
                    var expected = copy.GetMetadata("Sha256");
                    var actual = Sha256(Encoding.UTF8.GetBytes(text));
                    // The controller's own licence is copied as it is; a third party's is held to its hash.
                    if (!string.IsNullOrEmpty(expected) && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.LogError("Third-party notices: {0} hashes to {1}, not to the {2} its entry declares.", copy.ItemSpec, actual, expected);
                        return false;
                    }

                    WriteIfChanged(copy.GetMetadata("Destination"), text);
                }

                return !Log.HasLoggedErrors;
            }
            catch (Exception exception)
            {
                Log.LogErrorFromException(exception, true);
                return false;
            }
        }

        #endregion

        #region Tools

        private Entry Read(ITaskItem item, List<string> problems)
        {
            var entry = new Entry();
            entry.Name = item.ItemSpec;
            entry.Kind = item.GetMetadata("Kind");
            entry.Version = item.GetMetadata("Version");
            entry.License = item.GetMetadata("License");
            entry.Holder = item.GetMetadata("Holder");
            entry.Source = item.GetMetadata("Source");
            entry.Commit = item.GetMetadata("Commit");
            entry.Where = item.GetMetadata("Where");
            entry.What = item.GetMetadata("What");
            entry.TextSource = item.GetMetadata("TextSource");

            foreach (var required in new[] { "Kind", "License", "Source", "Where", "What", "TextFile", "TextSha256", "TextSource" })
            {
                if (string.IsNullOrEmpty(item.GetMetadata(required)))
                    problems.Add(entry.Name + " declares no " + required + ".");
            }

            var textFile = item.GetMetadata("TextFile");
            if (!string.IsNullOrEmpty(textFile))
            {
                if (!File.Exists(textFile))
                {
                    problems.Add(entry.Name + ": the licence text " + textFile + " is not there.");
                }
                else
                {
                    entry.Text = ReadText(textFile);
                    entry.TextSha256 = Sha256(Encoding.UTF8.GetBytes(entry.Text));
                    if (!string.Equals(entry.TextSha256, item.GetMetadata("TextSha256"), StringComparison.OrdinalIgnoreCase))
                        problems.Add(entry.Name + ": the licence text " + textFile + " hashes to " + entry.TextSha256 + ", not to the " + item.GetMetadata("TextSha256") + " its entry declares.");
                }
            }

            if (string.Equals(entry.Kind, KIND_PACKAGE, StringComparison.Ordinal))
                ReadPackage(item, entry, problems);

            return entry;
        }

        /// <summary>Holds a package component to the package the build restored, and its assembly to a file of that package.</summary>
        private void ReadPackage(ITaskItem item, Entry entry, List<string> problems)
        {
            entry.PackageId = item.GetMetadata("PackageId");
            entry.Assembly = item.GetMetadata("Assembly");
            if (string.IsNullOrEmpty(entry.PackageId) || string.IsNullOrEmpty(entry.Assembly))
            {
                problems.Add(entry.Name + " is a package and declares no PackageId or no Assembly.");
                return;
            }

            var folder = Path.Combine(PackagesRoot, entry.PackageId.ToLowerInvariant(), entry.Version);
            var nuspec = Path.Combine(folder, entry.PackageId.ToLowerInvariant() + ".nuspec");
            if (!File.Exists(nuspec))
            {
                problems.Add(entry.Name + ": package " + entry.PackageId + " " + entry.Version + " is not in the NuGet cache (" + nuspec + "). The notice is for that version: when the reference moves, the notice is reviewed and moves with it.");
                return;
            }

            var metadata = File.ReadAllText(nuspec, Encoding.UTF8);
            var version = Element(metadata, "version");
            var license = Match(metadata, "<license\\s+type=\"expression\"\\s*>([^<]*)</license>");
            var commit = Match(metadata, "<repository\\b[^>]*\\bcommit=\"([^\"]*)\"");
            entry.Authors = Element(metadata, "authors");
            entry.Copyright = Element(metadata, "copyright");

            if (!string.Equals(version, entry.Version, StringComparison.Ordinal))
                problems.Add(entry.Name + ": the package's nuspec states version '" + version + "', the notice " + entry.Version + ".");
            if (!string.Equals(license, entry.License, StringComparison.Ordinal))
                problems.Add(entry.Name + ": the package's nuspec states the licence '" + license + "', the notice '" + entry.License + "'.");
            if (!string.Equals(commit, entry.Commit, StringComparison.OrdinalIgnoreCase))
                problems.Add(entry.Name + ": the package's nuspec names the source commit '" + commit + "', the notice '" + entry.Commit + "': the licence text was taken at the notice's.");

            var staged = Path.Combine(AssemblyDirectory, entry.Assembly + ".dll");
            if (!File.Exists(staged))
            {
                problems.Add(entry.Name + ": " + entry.Assembly + ".dll is not in " + AssemblyDirectory + ".");
                return;
            }

            entry.AssemblySha256 = Sha256(File.ReadAllBytes(staged));
            var found = false;
            var library = Path.Combine(folder, "lib");
            if (Directory.Exists(library))
            {
                foreach (var candidate in Directory.GetFiles(library, entry.Assembly + ".dll", SearchOption.AllDirectories))
                {
                    if (string.Equals(Sha256(File.ReadAllBytes(candidate)), entry.AssemblySha256, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
                problems.Add(entry.Name + ": the " + entry.Assembly + ".dll beside the controller is not a file of package " + entry.PackageId + " " + entry.Version + ": the build resolved another version, or the file was changed.");
        }

        private void CheckModuleAssemblies(List<Entry> entries, List<string> problems)
        {
            foreach (var assembly in ModuleAssemblies ?? new ITaskItem[0])
            {
                var noticed = false;
                foreach (var entry in entries)
                {
                    if (string.Equals(entry.Kind, KIND_PACKAGE, StringComparison.Ordinal) && string.Equals(entry.Assembly, assembly.ItemSpec, StringComparison.OrdinalIgnoreCase))
                        noticed = true;
                }

                if (!noticed)
                    problems.Add("the module carries " + assembly.ItemSpec + ".dll and no component of the notices names it: add one (its package, version, licence, source commit and licence text) to CorpusNotices.targets.");
            }
        }

        private string Compose(List<Entry> entries)
        {
            var text = new StringBuilder();
            text.Append("THIRD-PARTY NOTICES\n\n");
            text.Append(Wrap(Subject + " " + SubjectVersion + " (" + ModuleName + ") redistributes the software and the data listed below. "
                + "Each is named with its version, its licence, where it comes from and which file of the module holds it; the licence texts follow, each written once. "
                + "The controller's own code is under the MIT licence, which travels with every copy: LICENSE.txt in the module, LICENSE in the package.")).Append('\n');

            text.Append("In the module:\n\n");
            foreach (var entry in entries)
                text.Append("  ").Append(Title(entry)).Append(" (").Append(entry.License).Append("): ").Append(entry.Where).Append('\n');

            if (DataAssets != null && DataAssets.Length > 0)
            {
                text.Append('\n').Append(Wrap("Not in the module: a node fetches these beside it as data assets, each an archive that carries its own notices, which this file does not repeat.")).Append('\n');
                foreach (var asset in DataAssets)
                {
                    text.Append("  ").Append(asset.ItemSpec).Append('\n');
                    text.Append("      ").Append(asset.GetMetadata("What")).Append('\n');
                    text.Append("      Notices: ").Append(asset.GetMetadata("Notice")).Append('\n');
                }
            }

            var written = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                text.Append('\n').Append(RULE).Append('\n');
                text.Append(Title(entry)).Append(" (").Append(entry.License).Append(")\n");
                text.Append(RULE).Append("\n\n");
                text.Append(Wrap(entry.What)).Append('\n');

                if (string.Equals(entry.Kind, KIND_PACKAGE, StringComparison.Ordinal))
                {
                    text.Append("Package:   ").Append(entry.PackageId).Append(' ').Append(entry.Version);
                    if (!string.IsNullOrEmpty(entry.Authors))
                        text.Append(", by ").Append(entry.Authors);
                    text.Append('\n');
                    if (!string.IsNullOrEmpty(entry.Copyright))
                        text.Append("Copyright: ").Append(entry.Copyright).Append(" (as the package states it)\n");
                    text.Append("File:      ").Append(entry.Assembly).Append(".dll, as the package ships it (SHA-256 ").Append(entry.AssemblySha256).Append(")\n");
                }
                else if (!string.IsNullOrEmpty(entry.Holder))
                {
                    text.Append("Copyright: ").Append(entry.Holder).Append('\n');
                }

                text.Append("Source:    ").Append(entry.Source);
                if (!string.IsNullOrEmpty(entry.Commit))
                    text.Append(" at ").Append(entry.Commit);
                text.Append('\n');
                text.Append("Licence:   ").Append(entry.License).Append("; the text below is ").Append(entry.TextSource).Append('\n');

                string first;
                if (written.TryGetValue(entry.TextSha256, out first))
                {
                    text.Append("\n-- the same text, word for word, as under ").Append(first).Append(" above\n");
                    continue;
                }

                written[entry.TextSha256] = Title(entry);
                text.Append('\n').Append(entry.Text.TrimEnd('\n')).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>A component's name with its version, when it has one.</summary>
        private static string Title(Entry entry)
        {
            return string.IsNullOrEmpty(entry.Version) ? entry.Name : entry.Name + " " + entry.Version;
        }

        /// <summary>A sentence wrapped at 86 columns.</summary>
        private static string Wrap(string sentence)
        {
            var text = new StringBuilder();
            var column = 0;
            foreach (var word in (sentence ?? "").Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (column > 0 && column + 1 + word.Length > 86)
                {
                    text.Append('\n');
                    column = 0;
                }

                if (column > 0)
                {
                    text.Append(' ');
                    column++;
                }

                text.Append(word);
                column += word.Length;
            }

            return text.Append('\n').ToString();
        }

        /// <summary>A file's text with its line ends as line feeds, whatever a checkout made of them; nothing else is touched.</summary>
        private static string ReadText(string path)
        {
            return new UTF8Encoding(false).GetString(File.ReadAllBytes(path)).Replace("\r\n", "\n");
        }

        private static void WriteIfChanged(string path, string text)
        {
            var bytes = new UTF8Encoding(false).GetBytes(text);
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == bytes.Length && string.Equals(Sha256(existing), Sha256(bytes), StringComparison.Ordinal))
                    return;
            }

            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllBytes(path, bytes);
        }

        private static string Element(string xml, string name)
        {
            return Match(xml, "<" + name + ">([^<]*)</" + name + ">");
        }

        private static string Match(string text, string pattern)
        {
            var match = Regex.Match(text, pattern);
            return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : "";
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var builder = new StringBuilder(64);
                foreach (var b in sha.ComputeHash(bytes))
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        #endregion

        #region Properties

        /// <summary>The components, in the order they are written. See CorpusNotices.targets for the metadata.</summary>
        [Required]
        public ITaskItem[] Components { get; set; }

        /// <summary>Licence files copied beside the notices with line feeds: Include = the source, Destination = the copy, Sha256 = of the text.</summary>
        public ITaskItem[] Copies { get; set; }

        /// <summary>The assemblies the module carries beside the controller's own (names without extension).</summary>
        public ITaskItem[] ModuleAssemblies { get; set; }

        /// <summary>The data assets a node resolves beside the module: Include = the id, ExtractTo, Notice = the notice files that arrive with it.</summary>
        public ITaskItem[] DataAssets { get; set; }

        /// <summary>The NuGet cache the build restored into.</summary>
        [Required]
        public string PackagesRoot { get; set; }

        /// <summary>The folder the build put the assemblies in.</summary>
        [Required]
        public string AssemblyDirectory { get; set; }

        /// <summary>The name of the package the notices are for.</summary>
        [Required]
        public string Subject { get; set; }

        /// <summary>Its version.</summary>
        [Required]
        public string SubjectVersion { get; set; }

        /// <summary>The module folder's name, as a node sees it.</summary>
        [Required]
        public string ModuleName { get; set; }

        /// <summary>The file to write.</summary>
        [Required]
        public string OutputFile { get; set; }

        #endregion

        #region Entry

        /// <summary>One component as it is written: what its item declares and what the build found.</summary>
        private sealed class Entry
        {
            #region Properties

            public string Name { get; set; }

            public string Kind { get; set; }

            public string Version { get; set; }

            public string License { get; set; }

            public string Holder { get; set; }

            public string Source { get; set; }

            public string Commit { get; set; }

            public string Where { get; set; }

            public string What { get; set; }

            public string TextSource { get; set; }

            public string Text { get; set; }

            public string TextSha256 { get; set; }

            public string PackageId { get; set; }

            public string Assembly { get; set; }

            public string AssemblySha256 { get; set; }

            public string Authors { get; set; }

            public string Copyright { get; set; }

            #endregion
        }

        #endregion
    }
}
