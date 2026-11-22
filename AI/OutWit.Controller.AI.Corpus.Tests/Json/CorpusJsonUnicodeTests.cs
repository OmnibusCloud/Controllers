using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Json;

/// <summary>
/// A string or a key whose text is no Unicode — the escape of half a surrogate pair, or bytes
/// that are not UTF-8 — in a document a lab hands in. The JSON library takes such a document for
/// well-formed and fails only where the text is read, with an exception of its own kind: it left
/// the manifest parser and the report reader as it was. A manifest lists it as a problem now,
/// and a report is refused in the words every other document that is no report is refused in.
/// </summary>
[TestFixture]
public sealed class CorpusJsonUnicodeTests
{
    #region Constants

    /// <summary>The escape of the first half of a surrogate pair, without its second half: a backslash, then <c>ud800</c>.</summary>
    private static readonly string HALF = (char)92 + "ud800";

    private const string NOT_UNICODE_IN_A_MANIFEST = "manifest: a string or a key is not Unicode text (an escape of half a surrogate pair, or bytes that are not UTF-8).";

    #endregion

    #region The manifest

    [Test]
    public void HalfASurrogatePairInAManifestIsListedNotThrownTest()
    {
        (string Name, string Manifest)[] manifests =
        [
            ("a source URL", Manifest(source: $"\"https://data.commoncrawl.org/a{HALF}.warc.wet.gz\"")),
            ("the URL of a source object", Manifest(source: $"{{\"url\":\"https://data.commoncrawl.org/a{HALF}.warc.wet.gz\",\"size\":10}}")),
            ("a key of a source object", Manifest(source: $"{{\"url\":\"https://data.commoncrawl.org/a.warc.wet.gz\",\"{HALF}\":10}}")),
            ("the pipeline's version", Manifest(pipeline: $"\"version\":\"v{HALF}\"")),
            ("a key of the pipeline", Manifest(pipeline: $"\"version\":\"v1\",\"{HALF}\":true")),
            ("the pipeline's output", Manifest(pipeline: $"\"version\":\"v1\",\"output\":\"{HALF}\"")),
            ("a target language", Manifest(pipeline: $"\"version\":\"v1\",\"target_languages\":[\"{HALF}\"]")),
            ("an allowed origin", Manifest(origin: $"data{HALF}.commoncrawl.org")),
            ("a key of the manifest", Manifest(extra: $",\"{HALF}\":1"))
        ];

        Assert.Multiple(() =>
        {
            Assert.That(CorpusManifestParser.Parse(Manifest()).Errors, Is.Empty, "the manifest the cases are made from");

            foreach (var (name, text) in manifests)
            {
                CorpusManifest? manifest = null;
                Assert.DoesNotThrow(() => manifest = CorpusManifestParser.Parse(text), name);

                Assert.That(manifest?.Errors, Is.EqualTo(new[] { NOT_UNICODE_IN_A_MANIFEST }), name);
                Assert.That(manifest?.Sources, Is.Empty, $"{name}: nothing of such a manifest is planned");
            }
        });
    }

    [Test]
    public void BytesThatAreNotUtf8InAManifestAreListedNotThrownTest()
    {
        // 0xFF is no byte of UTF-8, inside a string that is well-formed otherwise.
        var text = Manifest(pipeline: "\"version\":\"v1#\"");
        var bytes = Encoding.UTF8.GetBytes(text);
        bytes[Array.IndexOf(bytes, (byte)'#')] = 0xFF;

        CorpusManifest? manifest = null;
        Assert.DoesNotThrow(() => manifest = CorpusManifestParser.Parse(new MemoryStream(bytes)));

        Assert.Multiple(() =>
        {
            Assert.That(manifest!.Errors, Has.Count.EqualTo(1));
            Assert.That(manifest.Errors[0], Does.StartWith("manifest: "));
            Assert.That(manifest.Sources, Is.Empty);
        });
    }

    #endregion

    #region The report

    [Test]
    public void HalfASurrogatePairInAReportIsNoReportTest()
    {
        var report = Encoding.UTF8.GetString(Report());
        var refusal = $"The report is not a {CorpusReportWriter.SCHEMA} document.";

        (string Name, string Report)[] reports =
        [
            ("the schema", report.Replace($"\"schema\": \"{CorpusReportWriter.SCHEMA}\"", $"\"schema\": \"{CorpusReportWriter.SCHEMA}{HALF}\"")),
            ("a shard's verdict", report.Replace("\"verdict\": \"Completed\"", $"\"verdict\": \"Completed{HALF}\"")),
            ("a shard's signatures blob", report.Replace("\"signatures_blob\": \"", $"\"signatures_blob\": \"{HALF}")),
            ("a shard's signatures hash", report.Replace("\"signatures_sha256\": \"", $"\"signatures_sha256\": \"{HALF}")),
            ("the summary's output", report.Replace("\"output\": \"full\"", $"\"output\": \"full{HALF}\"")),
            ("a key of the report", report.Replace("\"schema\":", $"\"{HALF}\": 1, \"schema\":"))
        ];

        Assert.Multiple(() =>
        {
            Assert.That(CorpusDedupInput.Read(Encoding.UTF8.GetBytes(report)).Refusal, Is.Null, "the report the cases are made from");

            foreach (var (name, text) in reports)
            {
                Assert.That(text, Is.Not.EqualTo(report), $"{name}: the report was changed");
                Assert.That(() => CorpusDedupInput.Read(Encoding.UTF8.GetBytes(text)), Throws.InvalidOperationException.With.Message.EqualTo(refusal), name);
            }
        });
    }

    #endregion

    #region The reader

    [Test]
    public void TheReaderSaysWhatIsWrongWithSuchADocumentTest()
    {
        Assert.Multiple(() =>
        {
            // A key of the root: the reader itself reads it.
            Assert.That(() => Read($"{{\"{HALF}\":1,\"items\":[]}}", (_, _) => { }, (_, _) => { }), Throws.InstanceOf<JsonException>().With.Message.EqualTo(CorpusJsonStream.NOT_UNICODE));

            // Inside a value: whoever reads the string meets it.
            Assert.That(() => Read($"{{\"name\":\"{HALF}\",\"items\":[]}}", (_, value) => value.GetString(), (_, _) => { }), Throws.InstanceOf<JsonException>().With.Message.EqualTo(CorpusJsonStream.NOT_UNICODE));
            Assert.That(() => Read($"{{\"items\":[{{\"{HALF}\":1}}]}}", (_, _) => { }, (_, item) => item.EnumerateObject().Select(property => property.Name).ToList()), Throws.InstanceOf<JsonException>().With.Message.EqualTo(CorpusJsonStream.NOT_UNICODE));

            // A whole pair is text like any other.
            string? read = null;
            Assert.That(() => Read($"{{\"name\":\"{HALF}{(char)92}udc00\",\"items\":[]}}", (_, value) => read = value.GetString(), (_, _) => { }), Throws.Nothing);
            Assert.That(read, Is.EqualTo(char.ConvertFromUtf32(0x10000)));
        });
    }

    #endregion

    #region Tools

    private static string Manifest(string pipeline = "\"version\":\"v1\"", string origin = "data.commoncrawl.org", string source = "\"https://data.commoncrawl.org/a.warc.wet.gz\"", string extra = "")
    {
        return $"{{\"pipeline\":{{{pipeline}}},\"allowed_origins\":[\"{origin}\"],\"sources\":[{source}]{extra}}}";
    }

    /// <summary>The report of a run of two signed shards that kept its products.</summary>
    private static byte[] Report()
    {
        var tasks = CorpusTestRun.Tasks(2, CorpusTestRun.Signed());

        return CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[1])]));
    }

    private static bool Read(string document, Action<string, JsonElement> onProperty, Action<int, JsonElement> onItem)
    {
        return CorpusJsonStream.Read(new MemoryStream(Encoding.UTF8.GetBytes(document)), "items", onProperty, onItem);
    }

    #endregion
}
