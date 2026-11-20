using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Tests.Dedup;

/// <summary>
/// The report's reader looked for a shard listed twice and a blob named twice in tables keyed by
/// what the report states: a set of signature blob ids, whose hash code is the id's four words
/// combined, and a grouping by MinHash parameters. A report written for them puts every key in
/// one chain, up to the million shards a report may list. Both are found by sorting now; what
/// the report is refused for, and which parameters the run takes, are what they were.
/// </summary>
[TestFixture]
public sealed class CorpusDedupInputRepeatsTests
{
    #region Tests

    [Test]
    public void BlobIdsThatShareOneHashCodeCostWhatAnyOthersCostTest()
    {
        // 200,000 completed shards whose signature blob ids all have the same hash code.
        const int shards = 200_000;
        var random = new Random(7);
        var report = Report(Enumerable.Range(0, shards).Select(index => (index, SameHashCode(random), (MinHashParameters?)null)));

        var clock = Stopwatch.StartNew();
        var input = CorpusDedupInput.Read(new MemoryStream(report));
        var elapsed = clock.Elapsed;
        TestContext.Out.WriteLine($"{shards} shards read in {elapsed.TotalSeconds:F2} s");

        Assert.Multiple(() =>
        {
            Assert.That(input.Refusal, Is.Null);
            Assert.That(input.Shards, Has.Count.EqualTo(shards));
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(20)), "a sort, not a chain of 200,000 (it was minutes)");
        });
    }

    [Test]
    public void TheFirstRepeatInTheReportsOrderIsWhatItIsRefusedForTest()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var blobTwice = Read((0, a, null), (1, b, null), (2, a, null), (3, c, null));
        var indexTwice = Read((5, a, null), (6, b, null), (5, c, null));
        var indexFirst = Read((0, a, null), (1, b, null), (1, c, null), (2, a, null));
        var blobFirst = Read((0, a, null), (1, a, null), (0, b, null));

        Assert.Multiple(() =>
        {
            Assert.That(blobTwice.Refusal, Is.EqualTo("The report names the signatures blob of shard 2 more than once: every shard has a blob of its own."), "the shard that names it again");
            Assert.That(indexTwice.Refusal, Is.EqualTo("The report lists shard 5 more than once."));
            Assert.That(indexFirst.Refusal, Is.EqualTo("The report lists shard 1 more than once."), "the earlier of the two repeats");
            Assert.That(blobFirst.Refusal, Is.EqualTo("The report names the signatures blob of shard 1 more than once: every shard has a blob of its own."));
        });
    }

    [Test]
    public void TheRunTakesTheParametersOfMostOfItsShardsTest()
    {
        var other = new MinHashParameters(64, 5, 16);
        var third = new MinHashParameters(32, 5, 8);

        var most = Read((0, Guid.NewGuid(), other), (1, Guid.NewGuid(), null), (2, Guid.NewGuid(), null), (3, Guid.NewGuid(), third));
        var tie = Read((0, Guid.NewGuid(), other), (1, Guid.NewGuid(), null), (2, Guid.NewGuid(), null), (3, Guid.NewGuid(), other));
        var tieOtherWay = Read((0, Guid.NewGuid(), null), (1, Guid.NewGuid(), other), (2, Guid.NewGuid(), other), (3, Guid.NewGuid(), null));

        Assert.Multiple(() =>
        {
            Assert.That(most.Parameters, Is.EqualTo(MinHashParameters.DEFAULT));
            Assert.That(most.Excluded.Select(exclusion => exclusion.Index), Is.EqualTo(new[] { 0, 3 }));
            Assert.That(tie.Parameters, Is.EqualTo(other), "between equals, the set of the lowest index");
            Assert.That(tieOtherWay.Parameters, Is.EqualTo(MinHashParameters.DEFAULT));
        });
    }

    #endregion

    #region Tools

    private static CorpusDedupInput Read(params (int Index, Guid Blob, MinHashParameters? Parameters)[] shards)
    {
        return CorpusDedupInput.Read(new MemoryStream(Report(shards)));
    }

    /// <summary>A report of completed shards, one document each, signed.</summary>
    private static byte[] Report(IEnumerable<(int Index, Guid Blob, MinHashParameters? Parameters)> shards)
    {
        var json = new StringBuilder("{\"schema\":\"corpus-report/6\",\"summary\":{\"output\":\"full\"},\"shards\":[");
        var first = true;
        foreach (var (index, blob, parameters) in shards)
        {
            if (!first)
                json.Append(',');
            first = false;

            json.Append("{\"index\":").Append(index).Append(",\"verdict\":\"Completed\",\"signatures_blob\":\"").Append(blob.ToString("D")).Append("\",\"stats\":{\"documents_out\":1}");
            if (parameters != null)
                json.Append(",\"minhash\":{\"hashes\":").Append(parameters.Hashes).Append(",\"shingle_words\":").Append(parameters.ShingleWords).Append(",\"bands\":").Append(parameters.Bands).Append('}');
            json.Append('}');
        }

        return Encoding.UTF8.GetBytes(json.Append("]}").ToString());
    }

    /// <summary>A blob id at random whose hash code is the same as every other one made here: its four words combined are a constant.</summary>
    private static Guid SameHashCode(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        var a = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var b = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        var c = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], 0x5A5A5A5A ^ a ^ b ^ c);
        return new Guid(bytes);
    }

    #endregion
}
