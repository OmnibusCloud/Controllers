using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Pdf;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Diagnostics;

/// <summary>
/// What the module of a process says about itself: the version, the stage set reproducing its
/// pinned fingerprint (the PDF stage's contract in it), the parser and compressor versions, the
/// language-model facts held to the reference model's file, the origin rule of the machine, the
/// caps, the PDF extractor's pin and whether the process can run it.
/// </summary>
[TestFixture]
public sealed class CorpusRuntimeDiagnosticsTests
{
    #region Constants

    private const string MODEL_FIXTURE = "language-model-cc-2026-43-16.bin";

    #endregion

    #region Diagnostics Tests

    [Test]
    public void TheDiagnosticsSayVersionStagesParsersAndCapsTest()
    {
        var diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse("data.lab.example; mirror.lab.example"));

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.ControllerVersion, Does.Match("^[0-9]+[.][0-9]+[.][0-9]+"));
            Assert.That(diagnostics.RuntimeTarget, Does.Match("^(windows|linux|macos)-(x64|arm64)$"));
            Assert.That(diagnostics.Stages, Is.EqualTo(CorpusStageSet.STAGES));
            Assert.That(diagnostics.StageSetSha256, Is.EqualTo(CorpusStageSet.PINNED_SHA256));
            Assert.That(diagnostics.StagesVerified, Is.True);
            Assert.That(diagnostics.HtmlParser, Does.Match("^AngleSharp [0-9]+[.][0-9]+[.][0-9]+$"));
            Assert.That(diagnostics.Compressor, Does.Match("^ZstdSharp [0-9]+[.][0-9]+[.][0-9]+, level 9$"));
            Assert.That(diagnostics.LanguageModelFormat, Is.EqualTo("WITLID1"));
            Assert.That(diagnostics.ReferenceLanguageModelSha256, Is.EqualTo(CorpusReferenceLanguageModel.SHA256));
            Assert.That(diagnostics.PublicOrigins, Is.EqualTo(new[] { "data.commoncrawl.org" }));
            Assert.That(diagnostics.MachineOrigins, Is.EqualTo(new[] { "data.lab.example", "mirror.lab.example" }));
            Assert.That(diagnostics.MaxShardBytes, Is.EqualTo(1L << 30));
            Assert.That(diagnostics.DownloadsPerNode, Is.EqualTo(2));
            Assert.That(diagnostics.MemoryPerShardBytes, Is.EqualTo(256L * 1024 * 1024));
            Assert.That(diagnostics.PdfModuleSha256, Is.EqualTo(PdfStageLimits.MODULE_SHA256));
        });
    }

    [Test]
    public void TheDiagnosticsSayWhetherThePdfStageCanRunHereTest()
    {
        var diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse(null));

        // The tests' process finds the module in @Data when it is there (CorpusTestEnvironment).
        if (PdfTestModule.Path != null)
            Assert.That(diagnostics.PdfStageProblem, Is.Empty);
        else
            Assert.That(diagnostics.PdfStageProblem, Does.Contain(PdfModuleLocator.RelativePath).And.Not.Contain(Environment.MachineName));
    }

    [Test]
    public void AMachineThatListsNothingHasNoMachineOriginsTest()
    {
        var diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse(null));

        Assert.That(diagnostics.MachineOrigins, Is.Empty);
    }

    [Test]
    public void TheStageSetFingerprintIsThePinTest()
    {
        var (sha256, verified) = CorpusStageSet.Fingerprint();

        Assert.Multiple(() =>
        {
            Assert.That(verified, Is.True, $"the stage set composes to {sha256}: a stage changed its product (move the benchmark's pin and its unit with it) or the PDF stage's contract changed (a new limit: move the stage set's pin with it)");
            Assert.That(CorpusStageSet.PINNED_SHA256, Is.EqualTo(CorpusStageSet.Compose(CorpusBenchmark.EXPECTED_CONTENT_SHA256)), "the pin is the benchmark's reference product with this PDF stage");
        });
    }

    [Test]
    public void EveryLimitOfThePdfStageIsInTheFingerprintTest()
    {
        // The descriptor names the module and every limit; a changed value is a changed fingerprint.
        var descriptor = PdfStageLimits.DESCRIPTOR;

        Assert.Multiple(() =>
        {
            Assert.That(descriptor, Does.Contain(PdfStageLimits.MODULE_SHA256));
            foreach (var value in new long[] { PdfStageLimits.FUEL, PdfStageLimits.MEMORY_BYTES, PdfStageLimits.MAX_INPUT_BYTES, PdfStageLimits.MAX_TEXT_BYTES, PdfStageLimits.DOCUMENT_WORDS, PdfStageLimits.RUNNING_LINE_MIN_PAGES })
                Assert.That(descriptor, Does.Contain(value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(CorpusStageSet.Compose(CorpusBenchmark.EXPECTED_CONTENT_SHA256), Is.Not.EqualTo(CorpusBenchmark.EXPECTED_CONTENT_SHA256));
        });
    }

    [Test]
    public void TheStageOrderNamesEverySwitchTest()
    {
        Assert.That(CorpusStageSet.STAGES, Is.SupersetOf(new[] { "extract_pdf", "identify_language", "clean_lines", "filter_quality", "sign" }), "the manifest's switches are stage names");
    }

    [Test]
    public void TheDiagnosticsRoundTripThroughMemoryPackTest()
    {
        var diagnostics = CorpusRuntimeDiagnosticsBuilder.Build(ShardOriginPolicy.Parse("data.lab.example"));

        var back = MemoryPackSerializer.Deserialize<CorpusRuntimeDiagnosticsData>(MemoryPackSerializer.Serialize(diagnostics))!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(diagnostics), Is.True);
            Assert.That(diagnostics.Clone().Is(diagnostics), Is.True);
            Assert.That(ReferenceEquals(diagnostics.Clone().Stages, diagnostics.Stages), Is.False);
        });
    }

    #endregion

    #region Reference Model Tests

    [Test]
    public void TheReferenceModelFactsAreThoseOfTheFileTest()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", MODEL_FIXTURE);
        var bytes = File.ReadAllBytes(path);
        var model = LanguageModel.FromBytes(bytes);

        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);

        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(CorpusReferenceLanguageModel.SHA256));
            Assert.That(reader.ReadString(), Is.EqualTo(CorpusReferenceLanguageModel.FORMAT));
            Assert.That(model.Profiles.Select(profile => profile.Code), Is.EqualTo(CorpusReferenceLanguageModel.LANGUAGES));
        });
    }

    #endregion

    #region Shard Result Tests

    [Test]
    public void TheRuntimeTargetIsAPlatformNotAMachineTest()
    {
        var target = CorpusRuntimeDiagnosticsBuilder.RuntimeTarget();

        Assert.Multiple(() =>
        {
            Assert.That(target, Does.Match("^(windows|linux|macos)-(x64|arm64)$"));
            Assert.That(target, Does.Not.Contain(Environment.MachineName));
        });
    }

    #endregion
}
