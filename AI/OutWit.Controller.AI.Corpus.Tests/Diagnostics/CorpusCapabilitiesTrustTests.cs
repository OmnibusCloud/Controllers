using System.Text.Json;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Storage;
using OutWit.Controller.AI.Corpus.Text.Casing;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Diagnostics;

/// <summary>
/// The capabilities document (<c>corpus-capabilities/2</c>) states what an application needs to
/// read the documents of a hardened run: every fixed word that stands where a node's text once
/// stood, the copies a result rests on, the limits a hostile input meets, the case table. Each is
/// held to the code's own constant, so the document cannot drift from what the controller does.
/// </summary>
[TestFixture]
public sealed class CorpusCapabilitiesTrustTests
{
    #region Fields

    private JsonDocument m_document = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        var diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse(null));
        m_document = JsonDocument.Parse(CorpusCapabilitiesWriter.Write(diagnostics, null, CorpusStorageLimit.NONE));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_document.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public void TheDocumentNamesEveryCodeAResultMayCarryTest()
    {
        var trust = m_document.RootElement.GetProperty("trust");
        var codes = trust.GetProperty("verdict_codes");

        Assert.Multiple(() =>
        {
            foreach (var verdict in Enum.GetValues<CorpusShardVerdict>().Where(verdict => verdict != CorpusShardVerdict.NodeFault))
            {
                var listed = Strings(codes.GetProperty(verdict.ToString()));
                Assert.That(listed, Is.Ordered.Using<string>(StringComparer.Ordinal), verdict.ToString());
                foreach (var code in listed.Where(code => code != CorpusResultCodes.HTTP_PATTERN))
                    Assert.That(CorpusResultCodes.IsKnown(verdict, code), Is.True, $"{verdict}: {code}");
            }

            Assert.That(codes.TryGetProperty(nameof(CorpusShardVerdict.NodeFault), out _), Is.False, "a node fault is no verdict");
            Assert.That(Strings(codes.GetProperty("FetchFailed")), Is.EqualTo(new[] { "body_truncated", "hash_mismatch", "http_NNN" }));
            Assert.That(Strings(codes.GetProperty("Completed")), Is.EqualTo(new[] { "truncated" }));
            Assert.That(CorpusResultCodes.IsKnown(CorpusShardVerdict.FetchFailed, CorpusResultCodes.Http(404)), Is.True);
            Assert.That(CorpusResultCodes.IsKnown(CorpusShardVerdict.FetchFailed, CorpusResultCodes.HTTP_PATTERN), Is.False, "the pattern is how the list writes them, not a code");
            Assert.That(Strings(trust.GetProperty("fault_codes")), Is.EqualTo(CorpusResultCodes.FAULTS));
            Assert.That(Strings(trust.GetProperty("fault_codes")), Has.All.Matches<string>(code => CorpusResultCodes.IsKnown(CorpusShardVerdict.NodeFault, code)));
        });
    }

    [Test]
    public void TheDocumentSaysWhatAResultRestsOnTest()
    {
        var trust = m_document.RootElement.GetProperty("trust");

        Assert.Multiple(() =>
        {
            Assert.That(trust.GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(trust.GetProperty("dispute_copies").GetInt32(), Is.EqualTo(3));
            Assert.That(trust.GetProperty("dispute_quorum").GetInt32(), Is.EqualTo(2));
            Assert.That(Strings(trust.GetProperty("outcomes")), Is.EqualTo(new[] { "Disputed", "NoResult" }));
            Assert.That(Strings(trust.GetProperty("no_result_reasons")), Is.EqualTo(new[] { "node_fault", "rejected", "missing" }));
            Assert.That(Strings(trust.GetProperty("rejection_reasons")), Is.EqualTo(CorpusLedger.REJECTION_REASONS));
            Assert.That(Strings(trust.GetProperty("batch_refusals")), Is.EqualTo(new[] { "version", "pdf_sandbox", "disk_space", "disk", "internal" }));
            Assert.That(Strings(trust.GetProperty("dedup_exclusions")), Is.EqualTo(CorpusDedupExclusion.REASONS));
        });
    }

    [Test]
    public void TheDocumentStatesTheLimitsAHostileInputMeetsTest()
    {
        var root = m_document.RootElement;
        var ceilings = root.GetProperty("ceilings");
        var rules = root.GetProperty("drop_rules").EnumerateArray().ToDictionary(rule => rule.GetProperty("name").GetString()!, rule => rule.GetProperty("stage").GetString());

        Assert.Multiple(() =>
        {
            Assert.That(ceilings.GetProperty("record_bytes").GetInt64(), Is.EqualTo(64L << 20));
            Assert.That(ceilings.GetProperty("wet_record_bytes").GetInt64(), Is.EqualTo(8L << 20));
            Assert.That(ceilings.GetProperty("unpack_ratio").GetInt32(), Is.EqualTo(32));
            Assert.That(ceilings.GetProperty("unpacked_bytes_min").GetInt64(), Is.EqualTo(64L << 20));
            Assert.That(ceilings.GetProperty("unpacked_bytes_max").GetInt64(), Is.EqualTo(16L << 30));
            Assert.That(ceilings.GetProperty("html_decode_bytes").GetInt64(), Is.EqualTo(16L << 20));
            Assert.That(ceilings.GetProperty("html_elements").GetInt32(), Is.EqualTo(120_000));
            Assert.That(ceilings.GetProperty("language_model_bytes").GetInt64(), Is.EqualTo(16L << 20));
            Assert.That(root.GetProperty("defaults").GetProperty("shards_held_beyond_degree").GetInt32(), Is.EqualTo(ShardTaskRunner.FETCH_AHEAD));
            Assert.That(root.GetProperty("pdf").GetProperty("shard_fuel").GetInt64(), Is.EqualTo(100 * root.GetProperty("pdf").GetProperty("fuel").GetInt64()));
            foreach (var rule in new[] { "record_oversized", "unsupported_charset", "html_too_complex", "html_error", "pdf_shard_fuel" })
                Assert.That(rules.GetValueOrDefault(rule), Is.EqualTo(ShardDropRules.STAGE_EXTRACT), rule);
        });
    }

    [Test]
    public void TheDocumentNamesTheCaseTableTest()
    {
        var casing = m_document.RootElement.GetProperty("casing");

        Assert.Multiple(() =>
        {
            Assert.That(casing.GetProperty("unicode").GetString(), Is.EqualTo("16.0.0"));
            Assert.That(casing.GetProperty("mapping").GetString(), Is.EqualTo("simple_lowercase"));
            Assert.That(casing.GetProperty("table_sha256").GetString(), Is.EqualTo(CorpusCasing.TABLE_SHA256));
            Assert.That(CorpusStageSet.CASING, Does.Contain(CorpusCasing.TABLE_SHA256), "the same table is in the stage set's fingerprint");
        });
    }

    #endregion

    #region Tools

    private static List<string> Strings(JsonElement array)
    {
        return array.EnumerateArray().Select(item => item.GetString()!).ToList();
    }

    #endregion
}
