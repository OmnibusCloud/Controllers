using OutWit.Cloud.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Live.Tests.Docs;

/// <summary>
/// The two ways the controller's application page (<c>docs/application.md</c>) submits a run,
/// held to the code that takes them. The call through the SDK is compiled code
/// (<see cref="SmtApplicationExample"/>): the page's example is its body. The request document is
/// read by the platform's own reader of a request (<c>JobRequestParser</c>, what the server's door
/// runs), with the door's rules for a document (<see cref="SmtLiveDocumentDoor"/>): it reads as
/// SmtRun with the set's blob and the options of the call, as a document of their type. What else
/// a document may carry, and what the door refuses, is <see cref="SmtDocumentVocabularyTests"/>.
/// </summary>
[TestFixture]
public sealed class SmtApplicationPageTests
{
    #region Constants

    private const string PAGE = "AI/OutWit.Controller.AI.Smt/docs/application.md";
    private const string EXAMPLE = "AI/OutWit.Controller.AI.Smt.Live/Tests/Docs/SmtApplicationExample.cs";
    private const string SCRIPT = "AI/OutWit.Controller.AI.Smt/Scripts/SmtRun.wit";

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
    public void TheRequestOfThePageIsTheRunOfTheCallTest()
    {
        var submission = JobRequestParser.Parse(string.Join("\n", Block("request")), SmtLiveDocumentDoor.Resolve);
        var values = submission.Parameters!.GetValues();

        Assert.Multiple(() =>
        {
            Assert.That(Read(SCRIPT), Does.StartWith("Job:SmtRun(Blob:problems, SmtOptions:opts)"), "the script takes the set's blob and the options");
            Assert.That(submission.ScriptName, Is.EqualTo("SmtRun"));
            Assert.That(values, Has.Length.EqualTo(2));
            Assert.That(values[0], Is.TypeOf<Guid>(), "the set is the blob's id");
            Assert.That(values[1], Is.TypeOf<SmtOptionsData>(), "the options are a document of their type");
            Assert.That(Block("sdk"), Does.Contain("var options = new SmtOptionsData { HardCapSeconds = 30, SamplingRate = 0.25 };"), "the options of the call");
            Assert.That((values[1] as SmtOptionsData)?.Is(new SmtOptionsData { HardCapSeconds = 30, SamplingRate = 0.25 }), Is.True, "and the request's are the same");
        });
    }

    #endregion

    #region Tools

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
