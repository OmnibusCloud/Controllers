using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Dedup;

/// <summary>
/// The merge over real signatures: near-duplicate texts cluster across shards with the lowest
/// document kept, distinct texts stay apart, the threshold decides, the result is a function
/// of the signatures, and the masks pack and unpack losslessly.
/// </summary>
[TestFixture]
public sealed class CorpusDeduplicatorTests
{
    #region Tests

    [Test]
    public void NearDuplicatesClusterAcrossShardsWithTheLowestKeptTest()
    {
        var random = new Random(11);
        var originals = Enumerable.Range(0, 6).Select(_ => WarcSynth.Paragraphs(random, 400)).ToList();

        // Shard 0: six distinct documents. Shard 1: a light edit of each of the first three, then
        // three more distinct ones. Shard 2: a copy of original 4 and another light edit of 0.
        var shard0 = originals.Select(MinHashSigner.Sign).ToList();
        var shard1 = originals.Take(3).Select(text => MinHashSigner.Sign(Edit(text, random, 3))).Concat(Enumerable.Range(0, 3).Select(_ => MinHashSigner.Sign(WarcSynth.Paragraphs(random, 400)))).ToList();
        var shard2 = new List<ulong[]> { MinHashSigner.Sign(originals[4]), MinHashSigner.Sign(Edit(originals[0], random, 2)) };

        var result = new CorpusDeduplicator().Run([Shard(2, shard2), Shard(0, shard0), Shard(1, shard1)]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Documents, Is.EqualTo(14));
            Assert.That(result.Shards.Select(s => s.ShardIndex), Is.EqualTo(new[] { 0, 1, 2 }), "shards come out in index order");
            Assert.That(result.Shards[0].Keep, Is.All.True, "the first shard's documents are the representatives");
            Assert.That(result.Shards[1].Keep, Is.EqualTo(new[] { false, false, false, true, true, true }));
            Assert.That(result.Shards[2].Keep, Is.EqualTo(new[] { false, false }));
            Assert.That(result.Duplicates, Is.EqualTo(5));
            Assert.That(result.Clusters, Has.Count.EqualTo(4));
            Assert.That(result.Clusters[0], Is.EqualTo(new[] { new CorpusDocumentRef(0, 0), new CorpusDocumentRef(1, 0), new CorpusDocumentRef(2, 1) }), "both edits of document 0 join its cluster, the original first");
            Assert.That(result.Clusters.Select(c => c[0]), Is.Ordered);
        });
    }

    [Test]
    public void DistinctDocumentsStayApartTest()
    {
        var random = new Random(12);
        var signatures = Enumerable.Range(0, 200).Select(_ => MinHashSigner.Sign(WarcSynth.Paragraphs(random, 300))).ToList();

        var result = new CorpusDeduplicator().Run([Shard(0, signatures)]);

        Assert.Multiple(() =>
        {
            Assert.That(result.Duplicates, Is.Zero);
            Assert.That(result.Clusters, Is.Empty);
            Assert.That(result.Shards[0].Keep, Is.All.True);
        });
    }

    [Test]
    public void ThresholdDecidesTest()
    {
        var random = new Random(13);
        var text = WarcSynth.Paragraphs(random, 400);
        var edited = Edit(text, random, 6);
        var pair = new List<ulong[]> { MinHashSigner.Sign(text), MinHashSigner.Sign(edited) };
        var similarity = MinHashSigner.Similarity(pair[0], pair[1]);

        // Six edited words in 400 leave a pair around 0.85: well inside what 16 bands of 8 catch,
        // so only the threshold decides.
        var strict = new CorpusDeduplicator(Math.Min(0.99, similarity + 0.05)).Run([Shard(0, pair)]);
        var loose = new CorpusDeduplicator(Math.Max(0.05, similarity - 0.05)).Run([Shard(0, pair)]);

        Assert.Multiple(() =>
        {
            Assert.That(similarity, Is.InRange(0.75, 0.97), $"the edit should land near 0.85, got {similarity}");
            Assert.That(strict.Duplicates, Is.Zero);
            Assert.That(loose.Duplicates, Is.EqualTo(1));
        });
    }

    [Test]
    public void ResultIsAFunctionOfTheSignaturesTest()
    {
        var random = new Random(14);
        var texts = Enumerable.Range(0, 50).Select(_ => WarcSynth.Paragraphs(random, 400)).ToList();
        var shards = new List<CorpusDedupShard>
        {
            Shard(0, texts.Select(MinHashSigner.Sign).ToList()),
            Shard(1, texts.Select(text => MinHashSigner.Sign(Edit(text, random, 2))).ToList())
        };

        var first = new CorpusDeduplicator().Run(shards);
        var second = new CorpusDeduplicator().Run(shards.AsEnumerable().Reverse().ToList());

        Assert.Multiple(() =>
        {
            Assert.That(CorpusDedupWriter.Write(first), Is.EqualTo(CorpusDedupWriter.Write(second)));
            Assert.That(first.Duplicates, Is.EqualTo(50));
            Assert.That(first.Shards[1].Keep, Is.All.False);
        });
    }

    [Test]
    public void EmptyRunsAndEmptyShardsAreHandledTest()
    {
        var empty = new CorpusDeduplicator().Run([]);
        var blank = new CorpusDeduplicator().Run([Shard(0, []), Shard(1, [MinHashSigner.Sign("one two three four five six seven eight nine ten eleven twelve")])]);

        Assert.Multiple(() =>
        {
            Assert.That(empty.Documents, Is.Zero);
            Assert.That(blank.Documents, Is.EqualTo(1));
            Assert.That(blank.Shards.Select(shard => shard.Documents), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(blank.Shards[1].Keep, Is.All.True);
        });
    }

    [Test]
    public void TheCapacityIsCountedInDocumentsAsWellAsInValuesTest()
    {
        Assert.Multiple(() =>
        {
            // Up to 128 hashes the documents are the bound: shorter signatures buy no more of them.
            foreach (var hashes in new[] { 1, 2, 16, 64, 127, 128 })
                Assert.That(CorpusDeduplicator.Capacity(hashes), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS), $"{hashes} hash(es)");

            // Above that the values are: longer signatures buy fewer.
            Assert.That(CorpusDeduplicator.Capacity(129), Is.LessThan(CorpusDeduplicator.MAX_DOCUMENTS));
            Assert.That(CorpusDeduplicator.Capacity(256), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS / 2));
            Assert.That(CorpusDeduplicator.Capacity(MinHashSigner.MAX_HASHES), Is.EqualTo(CorpusDeduplicator.MAX_DOCUMENTS / 8));

            foreach (var hashes in new[] { 1, 64, 128, 256, MinHashSigner.MAX_HASHES })
                Assert.That(CorpusDeduplicator.Capacity(hashes) * hashes, Is.LessThanOrEqualTo(CorpusDeduplicator.MAX_SIGNATURE_VALUES), $"{hashes} hash(es)");
        });
    }

    [Test]
    public void MoreDocumentsThanAMergeHoldsAreRefusedWhateverTheirSignaturesTest()
    {
        // Signatures of one value: 4,000,001 documents are 32 MB of them, far inside the bound on
        // the values, and one document more than the merge has room for.
        var most = new CorpusDedupShard(0, Guid.NewGuid(), 1, new ulong[CorpusDeduplicator.MAX_DOCUMENTS]);
        var over = new CorpusDedupShard(0, Guid.NewGuid(), 1, new ulong[CorpusDeduplicator.MAX_DOCUMENTS + 1]);

        using var stopped = new CancellationTokenSource();
        stopped.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(() => new CorpusDeduplicator(0, 1).Run([over]), Throws.InvalidOperationException.With.Message.EqualTo("4000001 documents exceed the merge's capacity of 4000000."));

            // The most it holds is not refused: it gets as far as its first document.
            Assert.That(() => new CorpusDeduplicator(0, 1).Run([most], stopped.Token), Throws.InstanceOf<OperationCanceledException>());
        });
    }

    [Test]
    public void WhatAMergeHoldsForADocumentBesideItsSignatureIsBoundedTest()
    {
        // A million documents of one value each: 8 MB of signatures. What the merge allocates on
        // top of them is its own tables, and they grow with the documents.
        var documents = 1_000_000;
        var bytesPerDocument = 48;

        var shard = CorpusTestSignatures.Any(0, documents, 1, seed: 3);
        new CorpusDeduplicator(0, 1).Run([CorpusTestSignatures.Any(0, 1000, 1, seed: 4)]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = new CorpusDeduplicator(0, 1).Run([shard]);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        TestContext.Out.WriteLine($"{documents} documents of 1 hash: {allocated} bytes allocated by the merge, {(double)allocated / documents:F1} a document");

        Assert.Multiple(() =>
        {
            Assert.That(result.Documents, Is.EqualTo(documents));
            Assert.That(result.Duplicates, Is.Zero);
            Assert.That(allocated, Is.LessThanOrEqualTo((long)bytesPerDocument * documents), "bytes a document beside its signature");

            // So the most documents a merge holds cost it this much beside their signatures.
            Assert.That((long)bytesPerDocument * CorpusDeduplicator.MAX_DOCUMENTS, Is.LessThan(200L << 20));
        });
    }

    [Test]
    public void ABandKeyIsTheKeyOfThatBandTest()
    {
        var signature = MinHashSigner.Sign("one two three four five six seven eight nine ten eleven twelve");
        var keys = MinHashSigner.BandKeys(signature, 16);

        Assert.That(Enumerable.Range(0, 16).Select(band => MinHashSigner.BandKey(signature, band, 8)), Is.EqualTo(keys));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(13)]
    [TestCase(1000)]
    public void KeepMaskRoundTripsThroughTheBitsetTest(int documents)
    {
        var random = new Random(documents);
        var keep = Enumerable.Range(0, documents).Select(_ => random.Next(3) != 0).ToArray();

        var bitset = CorpusDedupWriter.ToBitset(keep);

        Assert.Multiple(() =>
        {
            Assert.That(bitset, Has.Length.EqualTo((documents + 7) / 8));
            Assert.That(CorpusDedupWriter.FromBitset(bitset, documents), Is.EqualTo(keep));
        });
    }

    #endregion

    #region Tools

    private static CorpusDedupShard Shard(int index, IReadOnlyList<ulong[]> signatures)
    {
        return CorpusDedupShard.Of(index, Guid.NewGuid(), signatures);
    }

    /// <summary>Replaces a few words so the text is a near-duplicate, not a copy.</summary>
    private static string Edit(string text, Random random, int words)
    {
        var tokens = text.Split(' ');
        for (var i = 0; i < words; i++)
            tokens[random.Next(tokens.Length)] = "edited" + random.Next(1000);
        return string.Join(' ', tokens);
    }

    #endregion
}
