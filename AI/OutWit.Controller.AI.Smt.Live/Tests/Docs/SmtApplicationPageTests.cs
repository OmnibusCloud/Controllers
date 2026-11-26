using System.Text.Json;
using OutWit.Cloud.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Docs;

/// <summary>
/// The two ways the controller's application page (<c>docs/application.md</c>) submits a run,
/// held to the code that takes them. The call through the SDK is compiled code
/// (<see cref="SmtApplicationExample"/>): the page's example is its body. The request document is
/// read by the platform's own reader of a request (<c>JobRequestParser</c>, what the server's door
/// runs), with the door's rule for a document type: a type no installed controller publishes is
/// refused. It reads as SmtRun with the set's blob and null options, which the controller's tests
/// run through the engine as the defaults; and the options as a document are refused, since the
/// model package publishes no document type.
/// </summary>
[TestFixture]
public sealed class SmtApplicationPageTests
{
    #region Constants

    private const string PAGE = "AI/OutWit.Controller.AI.Smt/docs/application.md";
    private const string EXAMPLE = "AI/OutWit.Controller.AI.Smt.Live/Tests/Docs/SmtApplicationExample.cs";
    private const string SCRIPT = "AI/OutWit.Controller.AI.Smt/Scripts/SmtRun.wit";

    /// <summary>The attribute a controller publishes a document type with, matched by its full name as the server's door matches it.</summary>
    private const string CONTRACT = "OutWit.Cloud.Documents.JobDocumentContractAttribute";

    private const string FENCE = "```";

    #endregion

    #region Call Tests

    [Test]
    public void TheCallOfThePageIsCompiledCodeTest()
    {
        var block = Block("sdk");
        var usings = block.Where(line => line.StartsWith("using ", StringComparison.Ordinal)).ToList();
        var body = Trim(block.Where(line => !line.StartsWith("using ", StringComparison.Ordinal)).ToList());

        var source = Read(EXAMPLE).Split('\n');
        var start = Array.FindIndex(source, line => line.Trim() == "// <example>");
        var end = Array.FindIndex(source, line => line.Trim() == "// </example>");
        var compiled = source[(start + 1)..end].ToList();
        var indent = compiled.Where(line => line.Trim().Length > 0).Min(line => line.Length - line.TrimStart().Length);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.GreaterThan(0).And.LessThan(end), "the example's markers");
            Assert.That(body, Is.EqualTo(compiled.Select(line => line.Length >= indent ? line[indent..] : line.Trim()).ToList()), "the page's call is the compiled example's body");
            Assert.That(usings, Is.SubsetOf(source), "and names the namespaces the example is compiled with");
        });
    }

    #endregion

    #region Request Tests

    [Test]
    public void TheRequestOfThePageIsSmtRunWithTheSetAndNoOptionsTest()
    {
        var submission = JobRequestParser.Parse(string.Join("\n", Block("request")), Door);
        var values = submission.Parameters!.GetValues();

        Assert.Multiple(() =>
        {
            Assert.That(Read(SCRIPT), Does.StartWith("Job:SmtRun(Blob:problems, SmtOptions:opts)"), "the script takes the set's blob and the options");
            Assert.That(submission.ScriptName, Is.EqualTo("SmtRun"));
            Assert.That(values, Has.Length.EqualTo(2));
            Assert.That(values[0], Is.TypeOf<Guid>(), "the set is the blob's id");
            Assert.That(values[1], Is.Null, "the options are null: the defaults");
        });
    }

    [Test]
    public void TheOptionsCannotTravelAsADocumentTest()
    {
        var options = JsonSerializer.Serialize(new { maxModels = 5 });
        var request = $"{{\"schemaVersion\": 1, \"script\": \"SmtRun\", \"parameters\": [{{\"kind\": \"uuid\", \"value\": \"{Guid.NewGuid()}\"}}, {{\"kind\": \"document\", \"type\": \"smt.options@1\", \"value\": {options}}}]}}";

        Assert.Multiple(() =>
        {
            Assert.That(Published(), Is.Empty, "the model package publishes no document type");
            Assert.That(() => JobRequestParser.Validate(request), Throws.Nothing, "the request is well formed: a native client forwards it");
            Assert.That(() => JobRequestParser.Parse(request, Door), Throws.TypeOf<JobRequestFormatException>().With.Message.Contains("is not published by any installed controller"),
                "and the server's door refuses the options as a document");
        });
    }

    #endregion

    #region Tools

    /// <summary>The server's door for a document type: the vocabulary is the published types of the installed controllers' model packages; any other is refused.</summary>
    private static object Door(string typeId, JsonElement value)
    {
        if (!Published().Contains(typeId))
            throw new JobRequestFormatException($"Document type '{typeId}' is not published by any installed controller.");

        throw new AssertionException($"'{typeId}' is published, and this test does not materialize it");
    }

    /// <summary>The document types the model package publishes, by the attribute's full name.</summary>
    private static HashSet<string> Published()
    {
        return typeof(SmtOptionsData).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributesData())
            .Where(attribute => attribute.AttributeType.FullName == CONTRACT)
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The lines of the page's block after an example's marker.</summary>
    private static List<string> Block(string name)
    {
        var lines = Read(PAGE).Split('\n');
        var marker = Array.IndexOf(lines, $"<!-- example: {name} -->");
        Assert.That(marker, Is.GreaterThanOrEqualTo(0), $"the page's example '{name}'");
        Assert.That(lines[marker + 1], Does.StartWith(FENCE), $"a fenced block right after the marker of '{name}'");

        var end = Array.FindIndex(lines, marker + 2, line => line == FENCE);
        return lines[(marker + 2)..end].ToList();
    }

    /// <summary>The lines without the blank ones at the start and at the end.</summary>
    private static List<string> Trim(List<string> lines)
    {
        return lines.SkipWhile(line => line.Trim().Length == 0).Reverse().SkipWhile(line => line.Trim().Length == 0).Reverse().ToList();
    }

    private static string Read(string path)
    {
        return File.ReadAllText(Path.Combine([SmtLiveFixtures.RepositoryRoot(), .. path.Split('/')])).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    #endregion
}
