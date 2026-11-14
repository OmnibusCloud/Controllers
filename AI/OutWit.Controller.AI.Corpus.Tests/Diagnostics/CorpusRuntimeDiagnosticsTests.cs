using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Diagnostics;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Diagnostics;

/// <summary>
/// What the module of a process says about itself: the version, the stage set reproducing its
/// pinned fingerprint, the parser and compressor versions, the language-model facts held to
/// the reference model's file, the origin rule of the machine, the caps.
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
            Assert.That(diagnostics.StageSetSha256, Is.EqualTo(CorpusBenchmark.EXPECTED_CONTENT_SHA256));
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
        });
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
            Assert.That(verified, Is.True, $"the reference shard hashes to {sha256}: a stage changed its product; move the benchmark's pin (and its unit) with it");
            Assert.That(CorpusStageSet.PINNED_SHA256, Is.EqualTo(CorpusBenchmark.EXPECTED_CONTENT_SHA256));
        });
    }

    [Test]
    public void TheStageOrderNamesEverySwitchTest()
    {
        Assert.That(CorpusStageSet.STAGES, Is.SupersetOf(new[] { "identify_language", "clean_lines", "filter_quality", "sign" }), "the manifest's switches are stage names");
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
