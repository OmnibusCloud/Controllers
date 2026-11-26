using System.Text.Json;
using System.Text.RegularExpressions;

namespace OutWit.Controller.AI.Smt.Tests.Docs;

/// <summary>
/// The documents a package carries are read where the package is: on nuget.org and in the
/// package's folder, where a relative link leads nowhere. So every link of them is an address of
/// the repository (or an anchor of the page), and every such address leads to a file the
/// repository has, and to a heading the file has. The schemas' <c>$id</c> is a name, not an
/// address: the READMEs say so, and the name is the schema's place in the repository.
/// </summary>
[TestFixture]
public sealed class SmtDocumentLinksTests
{
    #region Constants

    private const string REPOSITORY = "https://github.com/OmnibusCloud/Controllers/";
    private const string CONTROLLER_PROJECT = "AI/OutWit.Controller.AI.Smt/OutWit.Controller.AI.Smt.csproj";
    private const string DOCS = "AI/OutWit.Controller.AI.Smt/docs";
    private const string MODEL_DOCS = "AI/OutWit.Controller.AI.Smt.Model/docs";
    private const string README = "AI/OutWit.Controller.AI.Smt/README.md";
    private const string MODEL_README = "AI/OutWit.Controller.AI.Smt.Model/README.md";

    /// <summary>The documents the two packages carry: the controller's README and its two pages, and the model package's README.</summary>
    private static readonly string[] PACKED = [README, DOCS + "/capabilities.md", DOCS + "/application.md", MODEL_README];

    #endregion

    #region Link Tests

    [Test]
    public void PackedDocumentsLinkOnlyToAddressesTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var document in PACKED)
            {
                foreach (var link in SmtMarkdown.Links(SmtMarkdown.Read(document)))
                    Assert.That(link.StartsWith("https://", StringComparison.Ordinal) || link.StartsWith('#'), Is.True, $"{document}: '{link}' leads nowhere in a package");
            }
        });
    }

    [Test]
    public void EveryAddressOfTheRepositoryLeadsToItsFileTest()
    {
        var checkedLinks = 0;

        Assert.Multiple(() =>
        {
            foreach (var document in PACKED)
            {
                var text = SmtMarkdown.Read(document);
                foreach (var link in SmtMarkdown.Links(text))
                {
                    if (link.StartsWith('#'))
                    {
                        Assert.That(SmtMarkdown.Anchors(text), Does.Contain(link[1..]), $"{document}: the heading of '{link}'");
                        continue;
                    }

                    var address = Regex.Match(link, $"^{Regex.Escape(REPOSITORY)}(?<kind>blob|tree)/main/(?<path>[^#]+)(#(?<anchor>.+))?$");
                    if (!link.StartsWith(REPOSITORY, StringComparison.Ordinal))
                        continue;

                    Assert.That(address.Success, Is.True, $"{document}: '{link}' is an address of a file or a folder on main");
                    if (!address.Success)
                        continue;

                    checkedLinks++;
                    var path = SmtMarkdown.PathOf(address.Groups["path"].Value);
                    var folder = address.Groups["kind"].Value == "tree";
                    Assert.That(folder ? Directory.Exists(path) : File.Exists(path), Is.True, $"{document}: '{link}' leads to nothing in the repository");

                    if (address.Groups["anchor"].Success && File.Exists(path))
                        Assert.That(SmtMarkdown.Anchors(SmtMarkdown.Read(address.Groups["path"].Value)), Does.Contain(address.Groups["anchor"].Value), $"{document}: the heading of '{link}'");
                }
            }

            Assert.That(checkedLinks, Is.GreaterThan(20), "the packed documents link into the repository");
        });
    }

    #endregion

    #region Schema Tests

    [Test]
    public void EverySchemaIdIsItsPlaceAndTheReadmesSayItIsANameTest()
    {
        var schemas = Directory.GetFiles(SmtMarkdown.PathOf(DOCS), "*.schema.json").Select(file => $"{DOCS}/{Path.GetFileName(file)}")
            .Append($"{MODEL_DOCS}/smt-problems.schema.json").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(schemas, Has.Count.EqualTo(6), "the five documents' schemas and the problem set's");
            foreach (var schema in schemas)
            {
                using var document = JsonDocument.Parse(SmtMarkdown.Read(schema));
                Assert.That(document.RootElement.GetProperty("$id").GetString(), Is.EqualTo(REPOSITORY + schema), $"{schema}: its name is its place in the repository");
            }

            Assert.That(SmtMarkdown.Read(README).Replace("\n", " ", StringComparison.Ordinal), Does.Contain($"A schema's `$id` is `{REPOSITORY}{DOCS}/` and its file name: the schema's name, not an address that answers."));
            Assert.That(SmtMarkdown.Read(MODEL_README).Replace("\n", " ", StringComparison.Ordinal), Does.Contain($"Its `$id` (`{REPOSITORY}{MODEL_DOCS}/smt-problems.schema.json`) is the schema's name, not an address that answers."));
        });
    }

    [Test]
    public void TheControllerPacksEveryPageAndSchemaOfItsDocsTest()
    {
        var project = SmtMarkdown.Read(CONTROLLER_PROJECT);
        var packed = Regex.Matches(project, "<None Update=\"docs\\\\(?<file>[^\"]+)\" Pack=\"true\" PackagePath=\"docs\\\\(?<path>[^\"]+)\" />")
            .Where(match => match.Groups["file"].Value == match.Groups["path"].Value).Select(match => match.Groups["file"].Value);
        var files = Directory.GetFiles(SmtMarkdown.PathOf(DOCS)).Select(Path.GetFileName);

        Assert.That(packed, Is.EquivalentTo(files), "every page and schema of docs/ is packed under docs/, and nothing beside them");
    }

    #endregion
}
