using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Signatures;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Dedup;

/// <summary>
/// The merge's pass over a band, with signatures a signer would not give: a node writes the
/// signatures of its shard, and what it writes decides the band keys. The pass costs the same
/// whatever the keys are, it decides what a table keyed by the band keys decided (so no product of
/// a run moved with it), and it reads the token at every document.
/// </summary>
[TestFixture]
public sealed class CorpusDedupBandTests
{
    #region Constants

    private const int HASHES = MinHashSigner.HASHES;
    private const int BANDS = MinHashSigner.BANDS;

    /// <summary>The documents of the shard whose keys would share one chain of a table.</summary>
    private const int CHAINED_DOCUMENTS = 40_000;

    /// <summary>
    /// The seconds the merge of that shard may take. It takes a fraction of one; a table keyed by
    /// the band keys takes minutes for it, and four times as long for twice the documents.
    /// </summary>
    private const double CHAINED_SECONDS = 10;

    #endregion

    #region The cost of a band

    [Test]
    public void SignaturesWhoseKeysWouldShareOneChainOfATableCostWhatAnyOthersCostTest()
    {
        // One shard that was not sampled: every document has a band key of its own in every band,
        // and the two halves of every key are the same, so a table keyed by the keys would hold
        // all of a band in one chain. No two documents are alike.
        var chained = CorpusTestSignatures.WithKeysOfEqualHalves(0, CHAINED_DOCUMENTS, HASHES, BANDS, seed: 7);
        var others = CorpusTestSignatures.Any(0, CHAINED_DOCUMENTS, HASHES, seed: 5);

        var clock = Stopwatch.StartNew();
        var plain = new CorpusDeduplicator().Run([others]);
        var plainSeconds = clock.Elapsed.TotalSeconds;

        clock.Restart();
        var result = new CorpusDeduplicator().Run([chained]);
        var seconds = clock.Elapsed.TotalSeconds;

        TestContext.Out.WriteLine($"{CHAINED_DOCUMENTS} documents, {(long)CHAINED_DOCUMENTS * HASHES * sizeof(ulong)} bytes of signatures: {seconds:F2} s with keys of equal halves, {plainSeconds:F2} s with any others");

        Assert.Multiple(() =>
        {
            // The shard is what it is meant to be.
            for (var document = 0; document < 100; document++)
            {
                for (var band = 0; band < BANDS; band++)
                {
                    var key = MinHashSigner.BandKey(chained.Signature(document), band, HASHES / BANDS);
                    if (key != CorpusTestSignatures.KeyOfEqualHalves(document, band) || (uint)(key >> 32) != (uint)key)
                        Assert.Fail($"document {document}, band {band}: the key is {key:x16}");
                }
            }

            Assert.That(result.Documents, Is.EqualTo(CHAINED_DOCUMENTS));
            Assert.That(result.Duplicates, Is.Zero, "no two documents are alike");
            Assert.That(plain.Duplicates, Is.Zero);
            Assert.That(seconds, Is.LessThan(CHAINED_SECONDS), "the merge of a shard costs what its documents cost, whatever its signatures are");
        });
    }

    #endregion

    #region What the merge keeps

    [TestCase(128, 16, 0.8, 1500, 21)]
    [TestCase(128, 16, 0.6, 1500, 22)]
    [TestCase(64, 32, 0.5, 1200, 23)]
    [TestCase(8, 8, 0.5, 2000, 24)]
    [TestCase(8, 1, 0.8, 2000, 25)]
    [TestCase(1, 1, 0.8, 3000, 26)]
    public void TheMergeKeepsWhatATableKeyedByTheBandKeysKeptTest(int hashes, int bands, double threshold, int documents, int seed)
    {
        // Documents alone, copies, copies of copies with a few values changed (on both sides of
        // the threshold, so that a cluster is a chain and not a ball), texts shorter than a
        // shingle: over three shards given out of order.
        var shards = Planted(hashes, documents, seed);

        var kept = new CorpusDeduplicator(threshold, bands).Run(shards);
        var again = new CorpusDeduplicator(threshold, bands).Run(shards.AsEnumerable().Reverse().ToList());
        var measure = CorpusTestTableMerge.Run(shards, threshold, bands);

        Assert.Multiple(() =>
        {
            Assert.That(kept.Duplicates, Is.GreaterThan(0), "the run has duplicates to decide");
            Assert.That(kept.Shards.SelectMany(shard => shard.Keep), Has.Some.True);
            Assert.That(kept.Shards.Select(shard => shard.Keep), Is.EqualTo(measure.Shards.Select(shard => shard.Keep)), "the same documents are kept");
            Assert.That(kept.Clusters, Is.EqualTo(measure.Clusters), "in the same clusters");
            Assert.That(CorpusDedupWriter.Write(kept), Is.EqualTo(CorpusDedupWriter.Write(measure)), "the document of the merge is the same, byte for byte");
            Assert.That(CorpusDedupWriter.Write(again), Is.EqualTo(CorpusDedupWriter.Write(kept)), "and the same for the shards in another order");
        });
    }

    [Test]
    public void KeysThatWouldShareAChainDecideNothingOfWhatIsKeptTest()
    {
        // The shard of keys with equal halves, and in a second shard a copy of every tenth of its
        // documents: the copies are found, and nothing else is.
        var chained = CorpusTestSignatures.WithKeysOfEqualHalves(0, 3000, HASHES, BANDS, seed: 9);
        var copies = CorpusDedupShard.Of(1, Guid.NewGuid(), Enumerable.Range(0, 300).Select(index => chained.Signature(index * 10).ToArray()).ToList());

        var kept = new CorpusDeduplicator().Run([copies, chained]);
        var measure = CorpusTestTableMerge.Run([copies, chained]);

        Assert.Multiple(() =>
        {
            Assert.That(kept.Duplicates, Is.EqualTo(300));
            Assert.That(kept.Shards[0].Keep, Is.All.True);
            Assert.That(kept.Shards[1].Keep, Is.All.False);
            Assert.That(kept.Clusters.Select(cluster => (cluster[0], cluster[1])), Is.EqualTo(Enumerable.Range(0, 300).Select(index => (new CorpusDocumentRef(0, index * 10), new CorpusDocumentRef(1, index)))));
            Assert.That(CorpusDedupWriter.Write(kept), Is.EqualTo(CorpusDedupWriter.Write(measure)));
        });
    }

    #endregion

    #region A merge that was stopped

    [Test]
    public void BothHalvesOfThePassReadTheTokenAtADocumentTest()
    {
        // A hundred copies of one document: the pass over any band would make them one cluster.
        var signature = MinHashSigner.Sign("one two three four five six seven eight nine ten eleven twelve");
        var documents = new CorpusDedupDocuments([CorpusDedupShard.Of(0, Guid.NewGuid(), Enumerable.Repeat(signature, 100).ToList())]);
        var everyOne = Enumerable.Range(0, documents.Count).ToList();

        using var stopped = new CancellationTokenSource();
        stopped.Cancel();

        var pass = new CorpusDedupBand(documents.Count);
        var clusters = new CorpusDedupClusters(documents.Count);

        Assert.Multiple(() =>
        {
            Assert.That(() => pass.Read(documents, 0, HASHES / BANDS, stopped.Token), Throws.InstanceOf<OperationCanceledException>(), "the reading of a band's keys stops at a document");

            Assert.That(() => pass.Read(documents, 0, HASHES / BANDS, CancellationToken.None), Throws.Nothing);
            Assert.That(() => pass.Join(documents, clusters, CorpusDeduplicator.DEFAULT_THRESHOLD, stopped.Token), Throws.InstanceOf<OperationCanceledException>(), "the joining of a band's documents stops at a document");
            Assert.That(everyOne.Select(clusters.Lowest), Is.EqualTo(everyOne), "nothing was joined by the pass that was stopped");

            Assert.That(() => pass.Join(documents, clusters, CorpusDeduplicator.DEFAULT_THRESHOLD, CancellationToken.None), Throws.Nothing);
            Assert.That(everyOne.Select(clusters.Lowest), Is.All.Zero, "the pass that was not stopped makes them one cluster");
        });
    }

    [Test]
    public void AMergeThatWasStoppedIsLeftTest()
    {
        var shard = CorpusTestSignatures.Any(0, 200, HASHES, seed: 3);

        using var stopped = new CancellationTokenSource();
        stopped.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(() => new CorpusDeduplicator().Run([shard], stopped.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(() => new CorpusDeduplicator().Run([], stopped.Token), Throws.InstanceOf<OperationCanceledException>(), "a merge of nothing that was stopped is stopped too");
            Assert.That(new CorpusDeduplicator().Run([shard], CancellationToken.None).Documents, Is.EqualTo(200));
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Three shards of signatures with near-duplicates planted in them: a document is new, or a
    /// copy of an earlier one with up to a third of its values changed, or the signature of a
    /// text shorter than a shingle.
    /// </summary>
    private static List<CorpusDedupShard> Planted(int hashes, int documents, int seed)
    {
        var random = new Random(seed);
        var signatures = new List<ulong[]>(documents);

        // A signature of one value has no near-duplicate: its documents are copies or strangers.
        ulong Value() => hashes == 1 ? (ulong)random.Next(documents / 3) : (ulong)random.NextInt64();

        for (var document = 0; document < documents; document++)
        {
            var kind = random.Next(10);
            ulong[] signature;
            if (signatures.Count > 0 && kind < 5)
            {
                signature = (ulong[])signatures[random.Next(signatures.Count)].Clone();
                for (var changes = random.Next(hashes / 3 + 1); changes > 0; changes--)
                    signature[random.Next(hashes)] = Value();
            }
            else if (kind == 5)
            {
                signature = Enumerable.Repeat(MinHashSigner.EMPTY_SLOT, hashes).ToArray();
            }
            else
            {
                signature = Enumerable.Range(0, hashes).Select(_ => Value()).ToArray();
            }

            signatures.Add(signature);
        }

        var first = documents / 2;
        var second = documents / 3;

        return
        [
            CorpusDedupShard.Of(2, Guid.NewGuid(), signatures.Skip(first + second).ToList()),
            CorpusDedupShard.Of(0, Guid.NewGuid(), signatures.Take(first).ToList()),
            CorpusDedupShard.Of(1, Guid.NewGuid(), signatures.Skip(first).Take(second).ToList())
        ];
    }

    #endregion
}
