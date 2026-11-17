using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Dedup;

/// <summary>
/// Audit finding H-2: the merge reads a signatures blob only when it is the one integrity compared
/// (its bytes hash to the report's <c>signatures_sha256</c>) and holds one signature per document
/// of the product. A node that reported the honest hash and uploaded other signatures does not
/// get them merged; and (H-3) such a blob costs the merge that shard, never the run.
/// </summary>
[TestFixture]
public sealed class CorpusDedupLoaderH2Tests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_dedup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public async Task H2TheBlobIntegrityComparedIsLoadedTest()
    {
        var honest = Blob(MinHashParameters.DEFAULT, 300, 301);
        var source = Source(0, honest, documents: 2);

        var (shard, reason) = await CorpusDedupLoader.LoadAsync(source, Resolve(source, honest), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Is.Empty);
            Assert.That(shard!.Documents, Is.EqualTo(2));
            Assert.That(shard.Hashes, Is.EqualTo(128));
            Assert.That(shard.Signature(1).ToArray(), Is.EqualTo(MinHashSigner.Sign(WarcSynth.Paragraphs(new Random(301), 300))));
        });
    }

    [Test]
    public async Task H2OtherSignaturesUnderTheHonestHashAreNotMergedTest()
    {
        // The audit's reproduction: the report states the honest hash and two documents; the
        // uploaded blob holds five foreign signatures. CorpusDedupInput.Check accepted it.
        var honest = Blob(MinHashParameters.DEFAULT, 300, 301);
        var forged = Blob(MinHashParameters.DEFAULT, 100, 101, 102, 103, 104);
        var source = Source(0, honest, documents: 2);

        var (shard, reason) = await CorpusDedupLoader.LoadAsync(source, Resolve(source, forged), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(shard, Is.Null);
            Assert.That(reason, Is.EqualTo(CorpusDedupExclusion.BLOB_LENGTH), "five signatures where the product has two documents");
        });
    }

    [Test]
    public async Task H2AsManySignaturesOfOtherDocumentsAreNotMergedTest()
    {
        // The right count, the wrong signatures: copied from documents the node wants gone.
        var honest = Blob(MinHashParameters.DEFAULT, 300, 301);
        var forged = Blob(MinHashParameters.DEFAULT, 100, 101);
        var source = Source(0, honest, documents: 2);

        var (shard, reason) = await CorpusDedupLoader.LoadAsync(source, Resolve(source, forged), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(shard, Is.Null);
            Assert.That(reason, Is.EqualTo(CorpusDedupExclusion.BLOB_HASH));
        });
    }

    [Test]
    public async Task H2ABlobMadeOtherwiseOrMissingIsLeftOutTest()
    {
        var honest = Blob(MinHashParameters.DEFAULT, 300, 301);
        var source = Source(0, honest, documents: 2);

        var garbage = new byte[honest.Length];
        var otherParameters = Blob(new MinHashParameters(128, 7, 16), 300, 301)[4..];

        var ofGarbage = (await CorpusDedupLoader.LoadAsync(source, Resolve(source, garbage), CancellationToken.None)).Reason;
        var ofOtherParameters = (await CorpusDedupLoader.LoadAsync(source, Resolve(source, otherParameters), CancellationToken.None)).Reason;
        var ofNoFile = (await CorpusDedupLoader.LoadAsync(source, _ => Task.FromResult(Path.Combine(m_directory, "nowhere.bin")), CancellationToken.None)).Reason;
        var ofNoBlob = (await CorpusDedupLoader.LoadAsync(source, _ => throw new InvalidOperationException("no such blob"), CancellationToken.None)).Reason;

        Assert.Multiple(() =>
        {
            Assert.That(ofGarbage, Is.EqualTo(CorpusDedupExclusion.BLOB_HEADER));
            Assert.That(ofOtherParameters, Is.EqualTo(CorpusDedupExclusion.BLOB_HEADER), "a blob of another layout, cut to the right length");
            Assert.That(ofNoFile, Is.EqualTo(CorpusDedupExclusion.BLOB_MISSING));
            Assert.That(ofNoBlob, Is.EqualTo(CorpusDedupExclusion.BLOB_MISSING));
        });
    }

    [Test]
    public async Task H3OneBadBlobCostsThatShardNotTheMergeTest()
    {
        // Three shards; the middle one's node uploaded other signatures than it reported.
        var first = Blob(MinHashParameters.DEFAULT, 300, 301);
        var second = Blob(MinHashParameters.DEFAULT, 400, 401);
        var third = Blob(MinHashParameters.DEFAULT, 300, 501);
        var sources = new[] { Source(0, first, 2), Source(1, second, 2), Source(2, third, 2) };
        var files = new Dictionary<Guid, byte[]> { [sources[0].SignaturesBlobId] = first, [sources[1].SignaturesBlobId] = Blob(MinHashParameters.DEFAULT, 300, 301), [sources[2].SignaturesBlobId] = third };
        var input = new CorpusDedupInput(sources, MinHashParameters.DEFAULT, null);

        var (shards, excluded) = await CorpusDedupLoader.LoadAsync(input, blobId => Task.FromResult(Write(blobId, files[blobId])), CancellationToken.None);
        var result = new CorpusDeduplicator().Run(shards) with { Excluded = excluded };
        using var document = System.Text.Json.JsonDocument.Parse(OutWit.Controller.AI.Corpus.Reports.CorpusDedupWriter.Write(result));

        Assert.Multiple(() =>
        {
            Assert.That(shards.Select(shard => shard.ShardIndex), Is.EqualTo(new[] { 0, 2 }));
            Assert.That(excluded.Select(exclusion => (exclusion.Index, exclusion.Reason)), Is.EqualTo(new[] { (1, CorpusDedupExclusion.BLOB_HASH) }));
            Assert.That(result.Documents, Is.EqualTo(4));
            Assert.That(result.Duplicates, Is.EqualTo(1), "document 300 of shard 2 repeats shard 0's; the forged copy of shard 0's signatures in shard 1 masked nothing");
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("excluded").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("excluded")[0].GetProperty("index").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("excluded")[0].GetProperty("reason").GetString(), Is.EqualTo("blob_hash"));
            Assert.That(document.RootElement.GetProperty("shards").EnumerateArray().Select(shard => shard.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 2 }), "a shard left out has no mask");
        });
    }

    [Test]
    public async Task H2AReportWithoutAHashHoldsTheBlobToItsLengthAndHeaderTest()
    {
        var blob = Blob(MinHashParameters.DEFAULT, 300, 301);
        var source = new CorpusDedupSource(0, Guid.NewGuid(), MinHashParameters.DEFAULT, "", 2);

        var (shard, reason) = await CorpusDedupLoader.LoadAsync(source, Resolve(source, blob), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(reason, Is.Empty);
            Assert.That(shard!.Documents, Is.EqualTo(2));
        });
    }

    #endregion

    #region Tools

    /// <summary>A signatures blob as a node writes one: a header and the signature of a text per seed.</summary>
    private static byte[] Blob(MinHashParameters parameters, params int[] seeds)
    {
        using var stream = new MemoryStream();
        ShardSignatureWriter.WriteHeader(stream, parameters);
        foreach (var seed in seeds)
            ShardSignatureWriter.Write(stream, MinHashSigner.Sign(WarcSynth.Paragraphs(new Random(seed), 300), parameters.Hashes, parameters.ShingleWords));

        return stream.ToArray();
    }

    /// <summary>A shard as an honest report names it: the hash of the honest blob, the product's document count.</summary>
    private static CorpusDedupSource Source(int index, byte[] honestBlob, int documents)
    {
        return new CorpusDedupSource(index, Guid.NewGuid(), MinHashParameters.DEFAULT, Convert.ToHexStringLower(SHA256.HashData(honestBlob)), documents);
    }

    private Func<Guid, Task<string>> Resolve(CorpusDedupSource source, byte[] stored)
    {
        return blobId => Task.FromResult(Write(blobId, stored));
    }

    private string Write(Guid blobId, byte[] bytes)
    {
        var path = Path.Combine(m_directory, blobId.ToString("N"));
        File.WriteAllBytes(path, bytes);
        return path;
    }

    #endregion
}
