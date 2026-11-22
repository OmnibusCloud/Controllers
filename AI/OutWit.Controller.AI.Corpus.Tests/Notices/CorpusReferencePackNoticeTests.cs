using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Notices;

/// <summary>
/// The reference language pack carries data of others: its rule data holds FineWeb-2's
/// stop-word lists, for every language of the pack but English, and FineWeb-2's repetition
/// thresholds for Japanese and Chinese (Apache-2.0). The pack is the input of a job and no file
/// of the module, so the module's notices name it as carrying those lists; and wherever the
/// repository hands the pack out as a file — its fixtures folder, the output of the tests, the
/// models folder of <c>corpus-live</c> — that project's licence text is beside it. A project
/// that takes the pack to a new place fails here until the licence goes with it. The pack's
/// bytes are what they were: manifests pin its hash.
/// </summary>
[TestFixture]
public sealed class CorpusReferencePackNoticeTests
{
    #region Constants

    private const string CONTROLLER = "OutWit.Controller.AI.Corpus";
    private const string MODULE = "corpus.module";
    private const string NOTICES = "THIRD-PARTY-NOTICES.txt";
    private const string FIXTURES = "Fixtures";

    /// <summary>The pack's hash, as manifests pin it.</summary>
    private const string PACK_SHA256 = "74c6c348b897998ca947e286cd673f41d20e9efc3c242befc575a852d8d8dbd3";

    /// <summary>The licence text's hash, its line ends as line feeds: the one the notices' build holds the text to.</summary>
    private const string LICENCE_SHA256 = "c71d239df91726fc519c6eb72d318ec65820627232b2f796219e87dcf35d0ab4";

    /// <summary>What a stop-word file of the pack's source says of itself when its list is FineWeb-2's.</summary>
    private const string FROM_FINEWEB = "# Source: FineWeb-2 (huggingface/fineweb-2)";

    private const string COPY = "CopyToOutputDirectory";

    #endregion

    #region Tests

    [Test]
    public void ThePackCarriesFineWeb2sListsTest()
    {
        var languages = CorpusRepositoryFiles.PathOf("AI", $"{CONTROLLER}.Assets", "languages");
        var source = LanguagePackSource.Read(languages);
        var fromFineWeb = Directory.GetDirectories(languages)
            .Where(folder => File.ReadAllText(Path.Combine(folder, LanguagePackSource.STOP_WORDS_FILE)).Contains(FROM_FINEWEB, StringComparison.Ordinal))
            .Select(folder => Path.GetFileName(folder))
            .Order(StringComparer.Ordinal)
            .ToList();

        var pack = LanguageModel.Load(CorpusFixtures.PathOf(CorpusFixtures.PACK)).Rules;

        Assert.Multiple(() =>
        {
            Assert.That(Sha256(File.ReadAllBytes(CorpusFixtures.PathOf(CorpusFixtures.PACK))), Is.EqualTo(PACK_SHA256), "the pack is the bytes manifests pin");
            Assert.That(source.Languages, Has.Count.EqualTo(16));
            Assert.That(fromFineWeb, Has.Count.EqualTo(15), "every language of the pack but one");
            Assert.That(fromFineWeb, Does.Not.Contain("eng"), "English has Gopher's eight words");

            foreach (var language in source.Languages)
            {
                Assert.That(pack.TryGet(language.Code, out var carried), Is.True, language.Code);
                Assert.That(carried.SameAs(language), Is.True, $"{language.Code}: the pack holds the list and the thresholds of the source folder");
            }

            foreach (var code in fromFineWeb)
                Assert.That(pack.TryGet(code, out var carried) ? carried.StopWords.Count : 0, Is.GreaterThanOrEqualTo(8), $"{code}: a list of FineWeb-2 is in the pack (the shortest has eight words)");
        });
    }

    [Test]
    public void TheModulesNoticesNameThePackTest()
    {
        var notices = File.ReadAllText(Path.Combine(CorpusRigModules.Folder, MODULE, NOTICES));

        Assert.Multiple(() =>
        {
            Assert.That(notices, Does.Contain("\nFineWeb-2 stop-word lists and repetition thresholds (Apache-2.0)\n"));
            Assert.That(notices, Does.Contain(CorpusFixtures.PACK), "the pack by its file name");
            Assert.That(notices, Does.Contain(PACK_SHA256), "and by its hash");
            Assert.That(Flat(notices), Does.Contain("fifteen of its sixteen languages, every one but English"));
            Assert.That(Flat(notices), Does.Contain("where the repository hands it out as a file, this licence text is beside it"));
        });
    }

    [Test]
    public void TheLicenceIsBesideThePackInTheRepositoryAndInTheOutputTest()
    {
        var committed = CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Tests", FIXTURES, CorpusFixtures.PACK_LICENCE);
        var copied = Path.Combine(CorpusFixtures.Folder, CorpusFixtures.PACK_LICENCE);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(CorpusRepositoryFiles.PathOf("AI", $"{CONTROLLER}.Tests", FIXTURES, CorpusFixtures.PACK)), Is.True, "the pack's place in the repository");
            Assert.That(Sha256(Encoding.UTF8.GetBytes(committed)), Is.EqualTo(LICENCE_SHA256), "the text beside the pack is the one the notices carry");
            Assert.That(committed, Is.EqualTo(CorpusRepositoryFiles.ReadText("AI", $"{CONTROLLER}.Assets", "languages", CorpusFixtures.PACK_LICENCE)), "and the one beside the lists themselves");

            Assert.That(File.Exists(Path.Combine(CorpusFixtures.Folder, CorpusFixtures.PACK)), Is.True, "the build copies the pack to the output");
            Assert.That(File.Exists(copied), Is.True, "and the licence text with it");
            Assert.That(File.Exists(copied) ? File.ReadAllText(copied).Replace("\r\n", "\n") : "", Is.EqualTo(committed));
        });
    }

    [Test]
    public void EveryProjectThatTakesThePackTakesTheLicenceIntoTheSameFolderTest()
    {
        var takers = new List<string>();
        var bare = new List<string>();

        foreach (var project in Projects())
        {
            var items = Items(project).ToList();
            foreach (var folder in items.Where(item => item.Copied && Holds(item.Source, CorpusFixtures.PACK)).Select(item => Path.GetDirectoryName(item.Target) ?? ""))
            {
                var name = Path.GetRelativePath(CorpusRepositoryFiles.Root, project).Replace('\\', '/');
                takers.Add(name);

                var licence = Path.Combine(folder, CorpusFixtures.PACK_LICENCE);
                if (!items.Any(item => item.Copied && string.Equals(item.Target, licence, StringComparison.OrdinalIgnoreCase) && Holds(item.Source, CorpusFixtures.PACK_LICENCE) && IsTheLicence(item.Source)))
                    bare.Add($"{name}: the pack goes to '{folder}' and {CorpusFixtures.PACK_LICENCE} does not");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(takers, Does.Contain($"AI/{CONTROLLER}.Live/{CONTROLLER}.Live.csproj"), "corpus-live copies the pack to its models folder: the scan sees it");
            Assert.That(takers, Does.Contain($"AI/{CONTROLLER}.Tests/{CONTROLLER}.Tests.csproj"), "the tests copy the pack to their output: the scan sees it");
            Assert.That(bare, Is.Empty, "the pack's rule data holds FineWeb-2's lists: the licence text goes wherever the pack goes");
        });
    }

    #endregion

    #region Tools

    /// <summary>The project files of the controllers and of the build: where an item that copies a file is written.</summary>
    private static IEnumerable<string> Projects()
    {
        var depth = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 };
        var folders = new[] { CorpusRepositoryFiles.PathOf("AI"), CorpusRepositoryFiles.PathOf("Build") };

        return folders
            .Where(Directory.Exists)
            .SelectMany(folder => new[] { "*.csproj", "*.targets", "*.props" }.SelectMany(pattern => Directory.EnumerateFiles(folder, pattern, depth)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// The items of a project file that name files: for each, the files it takes (the folder and
    /// the name or pattern of its <c>Include</c> or <c>Update</c>), where they go in the output
    /// (its <c>Link</c>, or the path as written) and whether it copies them there. An item is
    /// read as written: one that names the pack, or a pattern over the pack's own folder, is
    /// seen; a path made of properties or a pattern over folders (<c>**</c>) is not.
    /// </summary>
    private static IEnumerable<((string Folder, string Pattern) Source, string Target, bool Copied)> Items(string project)
    {
        var folder = Path.GetDirectoryName(project)!;

        foreach (var item in XDocument.Load(project).Descendants().Where(element => element.Parent?.Name.LocalName == "ItemGroup"))
        {
            var spec = (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update");
            if (spec == null || spec.Contains('$') || spec.Contains('@'))
                continue;

            var path = Path.GetFullPath(Path.Combine(folder, spec.Replace('\\', Path.DirectorySeparatorChar)));
            var target = ((string?)item.Attribute("Link") ?? spec).Replace('\\', Path.DirectorySeparatorChar);
            var copied = item.Attribute(COPY) != null || item.Elements().Any(child => child.Name.LocalName == COPY);

            yield return ((Path.GetDirectoryName(path)!, Path.GetFileName(path)), target, copied);
        }
    }

    /// <summary>Whether an item's files include a file of a name: its pattern matches the name, and the file is in its folder.</summary>
    private static bool Holds((string Folder, string Pattern) source, string name)
    {
        return FileSystemName.MatchesSimpleExpression(source.Pattern, name) && File.Exists(Path.Combine(source.Folder, name));
    }

    private static bool IsTheLicence((string Folder, string Pattern) source)
    {
        var text = File.ReadAllText(Path.Combine(source.Folder, CorpusFixtures.PACK_LICENCE)).Replace("\r\n", "\n");
        return Sha256(Encoding.UTF8.GetBytes(text)) == LICENCE_SHA256;
    }

    /// <summary>A text with its line breaks and indents as single spaces: a sentence is found wherever its lines were broken.</summary>
    private static string Flat(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    #endregion
}
