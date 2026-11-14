using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Docs;

namespace OutWit.Controller.AI.Corpus.Tests.Reports;

/// <summary>
/// The signatures in the integrity document (<c>corpus-integrity/4</c>): two executions that agree
/// on the text and disagree on the signatures are a mismatch of their own kind, named and counted;
/// a pair whose sides made no signature says there was nothing to compare; a side of an older
/// controller without the hash falls under the version-skew rule, never a mismatch.
/// </summary>
[TestFixture]
public sealed class CorpusIntegritySignaturesTests
{
    #region Constants

    private const string CONTENT = "0dbca5f50ef20873463d1d27865c16c3a038bd1309b6ca2abc401e6b6fd0c133";
    private const string SIGNATURES = "aa77d782313aba066db3054d44013b5c6d4b8b9a7004a90893acb963f93a1add";
    private const string OTHER_SIGNATURES = "e41a88f35141c9840f07f9decfb2f64c6bedf35e31eb09cc7e042d2f8bde998b";

    #endregion

    #region Tests

    [Test]
    public void SignaturesThatAgreeAreComparedAndMatchTest()
    {
        using var document = Write(Execution(0, "0.3.0", SIGNATURES), Execution(0, "0.3.0", SIGNATURES));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-integrity/4"));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signatures_compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signature_mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.True);
            Assert.That(pair.GetProperty("signatures").GetString(), Is.EqualTo(CorpusIntegrityWriter.SIGNATURES_MATCH));
            Assert.That(pair.GetProperty("primary").GetProperty("signatures_sha256").GetString(), Is.EqualTo(SIGNATURES));
        });
    }

    [Test]
    public void SignaturesThatDifferAreAMismatchOfTheirOwnKindTest()
    {
        // The text agrees: only the signatures tell the dishonest side apart.
        using var document = Write(Execution(4, "0.3.0", SIGNATURES), Execution(4, "0.3.0", OTHER_SIGNATURES));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signature_mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(1.0));
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pair.GetProperty("signatures").GetString(), Is.EqualTo(CorpusIntegrityWriter.SIGNATURES_DIFFER));
            Assert.That(pair.GetProperty("differs").EnumerateArray().Select(field => field.GetString()), Is.EqualTo(new[] { "signatures" }));
        });
    }

    [Test]
    public void APairWithoutSignaturesHasNothingOfTheKindToCompareTest()
    {
        using var document = Write(Execution(1, "0.3.0", ""), Execution(1, "0.3.0", ""));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("signatures_compared").GetInt32(), Is.Zero, "the pipeline does not sign");
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.True);
            Assert.That(pair.GetProperty("signatures").GetString(), Is.EqualTo(CorpusIntegrityWriter.SIGNATURES_NONE));
        });
    }

    [Test]
    public void AnOlderSideWithoutTheHashIsVersionSkewNotAMismatchTest()
    {
        using var document = Write(Execution(2, "0.3.0", SIGNATURES), Execution(2, "0.2.1", ""));
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("version_skew").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("signature_mismatches").GetInt32(), Is.Zero);
            Assert.That(pair.GetProperty("compared").GetBoolean(), Is.False);
            Assert.That(pair.TryGetProperty("signatures", out _), Is.False, "said only of a compared pair");
        });
    }

    [Test]
    public void TheDocumentValidatesAgainstItsSchemaTest()
    {
        using var document = Write(
            Execution(0, "0.3.0", SIGNATURES), Execution(0, "0.3.0", OTHER_SIGNATURES),
            Execution(1, "0.3.0", ""), Execution(1, "0.3.0", ""),
            Execution(2, "0.3.0", SIGNATURES), Execution(2, "0.2.1", ""),
            Execution(3, "0.3.0", SIGNATURES), Execution(3, "0.3.0", SIGNATURES));
        using var schema = JsonDocument.Parse(File.ReadAllBytes(SchemaPath()));

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSchemaCheck.Validate(schema.RootElement, document.RootElement), Is.Empty);
            Assert.That(schema.RootElement.GetProperty("properties").GetProperty("schema").GetProperty("const").GetString(), Is.EqualTo(CorpusIntegrityWriter.SCHEMA));
        });
    }

    #endregion

    #region Tools

    private static JsonDocument Write(params CorpusResultData[] executions)
    {
        return JsonDocument.Parse(CorpusIntegrityWriter.Write(executions));
    }

    private static CorpusResultData Execution(int index, string controller, string signatures)
    {
        return new CorpusResultData
        {
            TaskIndex = index,
            Verdict = CorpusShardVerdict.Completed,
            ContentSha256 = CONTENT,
            SignaturesSha256 = signatures,
            ControllerVersion = controller,
            Stats = new CorpusShardStatsData { RecordsIn = 10, DocumentsExtracted = 9, DocumentsOut = 5 }
        };
    }

    private static string SchemaPath()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "OutWit.slnx")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
            Assert.Ignore("Solution root not found");

        return Path.Combine(dir!, "AI", "OutWit.Controller.AI.Corpus", "docs", "corpus-integrity.schema.json");
    }

    #endregion
}
