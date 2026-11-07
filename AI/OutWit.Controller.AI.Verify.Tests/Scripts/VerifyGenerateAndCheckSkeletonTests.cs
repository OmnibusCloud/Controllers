using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// The A3 shape end to end: generators fan out, their outputs become check tasks against one
/// checker, the checks fan out, and the job returns the check report, the corpus of candidates
/// and the provenance — through the engine on the mock nodes.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyGenerateAndCheckSkeletonTests : VerifyScriptTestBase
{
    #region Constants

    // A seeded arithmetic generator: prints "<expr> = <value>"; seeds come in as argv[1].
    private const string GENERATOR =
        "import sys\n" +
        "x = int(sys.argv[1]) if len(sys.argv) > 1 else 1\n" +
        "def nxt():\n" +
        "    global x\n" +
        "    x ^= (x << 13) & 0xFFFFFFFF; x ^= x >> 17; x ^= (x << 5) & 0xFFFFFFFF; x &= 0xFFFFFFFF\n" +
        "    return x\n" +
        "ops = ['+', '-', '*']\n" +
        "n = 3 + nxt() % 4\n" +
        "expr = str(nxt() % 100)\n" +
        "for _ in range(n):\n" +
        "    expr += ' ' + ops[nxt() % 3] + ' ' + str(nxt() % 100)\n" +
        "print(expr + ' = ' + str(eval(expr)))\n";

    // The checker: reads one "<expr> = <value>" line and prints its verdict; it exits 0 either way
    // (a non-zero exit would mean the checker itself crashed, reported as RuntimeError).
    private const string CHECKER =
        "import sys\n" +
        "line = sys.stdin.readline().strip()\n" +
        "expr, _, value = line.rpartition(' = ')\n" +
        "ok = str(eval(expr, {'__builtins__': {}}, {})) == value\n" +
        "print('OK' if ok else 'MISMATCH ' + line)\n";

    #endregion

    #region Fields

    private string m_generateScript = null!;
    private string m_generateAndCheckScript = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_generateScript = LoadScript("VerifyGenerate.wit");
        m_generateAndCheckScript = LoadScript("VerifyGenerateAndCheck.wit");
    }

    #endregion

    #region Tests

    [Test]
    public void BundledScriptsCompileTest()
    {
        var generate = Engine.Compile(m_generateScript);
        var generateAndCheck = Engine.Compile(m_generateAndCheckScript);

        Assert.Multiple(() =>
        {
            Assert.That(generate.Variables.Select(v => v.Name), Is.SupersetOf(new[] { "taskset", "opts", "corpus", "report", "provenance" }));
            Assert.That(generateAndCheck.Variables.Select(v => v.Name),
                Is.SupersetOf(new[] { "generators", "checker", "opts", "candidates", "checkBatches", "verdicts", "report", "corpus", "provenance" }));
        });
    }

    [Test]
    public async Task GeneratedCandidatesAreCheckedUnderTheirOwnIndicesTest()
    {
        // Four generators: two honest, one that lies, one that crashes.
        var generators = new StringBuilder();
        generators.AppendLine(GeneratorLine(0, GENERATOR, "7"));
        generators.AppendLine(GeneratorLine(1, GENERATOR, "11"));
        generators.AppendLine(GeneratorLine(2, "print('2 + 2 = 5')", "0"));
        generators.AppendLine(GeneratorLine(3, "raise SystemExit(9)", "0"));

        var generatorsBlobId = await UploadAsync(generators.ToString(), "generators.jsonl");
        var checkerBlobId = await UploadAsync(CheckerSpec(), "checker.json");

        var job = Engine.Compile(m_generateAndCheckScript);
        var status = await Engine.ScheduleAndWaitAsync(job, generatorsBlobId, checkerBlobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        var summary = report.RootElement.GetProperty("summary");
        var verdictByIndex = report.RootElement.GetProperty("results").EnumerateArray()
            .ToDictionary(r => r.GetProperty("index").GetInt32(), r => r.GetProperty("verdict").GetString());

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("total").GetInt32(), Is.EqualTo(3), "the crashed generator has no check");
            Assert.That(summary.GetProperty("pass").GetInt32(), Is.EqualTo(2));
            Assert.That(verdictByIndex[0], Is.EqualTo("Pass"));
            Assert.That(verdictByIndex[1], Is.EqualTo("Pass"));
            Assert.That(verdictByIndex[2], Is.EqualTo("Fail"), "2 + 2 = 5 does not check");
            Assert.That(verdictByIndex.ContainsKey(3), Is.False);
        });

        var corpus = (await ReadTextAsync((Guid)job.Variables["corpus"].Value!))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(corpus, Has.Count.EqualTo(4), "the corpus keeps every candidate, checked or not");
            Assert.That(corpus[0].GetProperty("stdout").GetString(), Does.Contain(" = "));
            Assert.That(corpus[2].GetProperty("stdout").GetString(), Is.EqualTo("2 + 2 = 5\n"));
            Assert.That(corpus[3].GetProperty("verdict").GetString(), Is.EqualTo("RuntimeError"));
        });
    }

    [Test]
    public async Task GenerateReturnsTheCorpusOfOutputsTest()
    {
        var taskset = GeneratorLine(0, GENERATOR, "3") + "\n" + GeneratorLine(1, GENERATOR, "5") + "\n";
        var tasksetBlobId = await UploadAsync(taskset, "generators.jsonl");

        var job = Engine.Compile(m_generateScript);
        var status = await Engine.ScheduleAndWaitAsync(job, tasksetBlobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        var corpus = (await ReadTextAsync((Guid)job.Variables["corpus"].Value!))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.That(corpus, Has.Length.EqualTo(2));
        Assert.That(corpus.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("verdict").GetString()), Is.All.EqualTo("Pass"));
    }

    #endregion

    #region Tools

    private static string GeneratorLine(int index, string program, string seed)
    {
        var source = JsonSerializer.Serialize(program);
        return $$"""{"index":{{index}},"runtime":"python-3.14.6","entry":"gen.py","sources":{"gen.py":{{source}}},"args":["{{seed}}"]}""";
    }

    private static string CheckerSpec()
    {
        var source = JsonSerializer.Serialize(CHECKER);
        return $$"""{"runtime":"python-3.14.6","entry":"check.py","sources":{"check.py":{{source}}},"expected_stdout":"OK\n","expected_exit":0}""";
    }

    #endregion
}
