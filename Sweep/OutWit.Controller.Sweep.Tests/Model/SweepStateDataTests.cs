using MemoryPack;
using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Tests.Model;

/// <summary>
/// The chunk schedule appended to the sweep state in Sweep.Model 2.2.0 must
/// be invisible to every reader of a 2.0 - 2.1 payload: the default MemoryPack
/// format leaves an absent trailing member at its CLR default (null for a
/// list), and a reader that met such a null once died on it in production
/// (the 0.2.0 result index). The model owns the guarantee, not its callers.
/// </summary>
[TestFixture]
public sealed partial class SweepStateDataTests
{
    /// <summary>The 2.0 - 2.1 wire shape of the state: seven members, no schedule.</summary>
    [MemoryPackable]
    private sealed partial class LegacySweepState
    {
        [MemoryPackOrder(0)] public int ChunkIndex { get; set; }
        [MemoryPackOrder(1)] public int NextVariantOrdinal { get; set; }
        [MemoryPackOrder(2)] public int SucceededCount { get; set; }
        [MemoryPackOrder(3)] public int FailedCount { get; set; }
        [MemoryPackOrder(4)] public int RefusedCount { get; set; }
        [MemoryPackOrder(5)] public Guid? ManifestBlobId { get; set; }
        [MemoryPackOrder(6)] public List<SweepResultIndexEntryData> Results { get; set; } = [];
    }

    #region Legacy Payload Tests

    [Test]
    public void ALegacyPayloadReadsWithAnEmptyScheduleTest()
    {
        var manifest = Guid.NewGuid();
        var legacy = MemoryPackSerializer.Serialize(new LegacySweepState
        {
            ChunkIndex = 2, NextVariantOrdinal = 5, SucceededCount = 4, FailedCount = 1, ManifestBlobId = manifest,
            Results = [new SweepResultIndexEntryData { VariantIndex = 0, Outcome = SweepOutcome.Succeeded }]
        });

        var state = MemoryPackSerializer.Deserialize<SweepStateData>(legacy);

        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ChunkIndex, Is.EqualTo(2));
        Assert.That(state.ManifestBlobId, Is.EqualTo(manifest));
        Assert.That(state.Results, Has.Count.EqualTo(1));
        Assert.That(state.ChunkSizes, Is.Not.Null.And.Empty);
        Assert.That(state.ToString(), Does.StartWith("chunk 2:"), "no schedule, no 'of N'");

        // Is and Clone walk the schedule: neither may hit the null the wire left.
        var clone = state.Clone();
        Assert.That(clone.Is(state), Is.True);
        Assert.That(clone.ChunkSizes, Is.Empty);
    }

    [Test]
    public void TheScheduleRoundTripsAndTakesPartInEqualityTest()
    {
        var state = new SweepStateData { ChunkIndex = 1, NextVariantOrdinal = 3, SucceededCount = 3, ChunkSizes = [3, 3, 1] };

        var read = MemoryPackSerializer.Deserialize<SweepStateData>(MemoryPackSerializer.Serialize(state));

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }));
        Assert.That(read.Is(state), Is.True);
        Assert.That(read.ToString(), Does.StartWith("chunk 1 of 3:"));

        var other = state.Clone();
        other.ChunkSizes = [2, 2, 2, 1];
        Assert.That(other.Is(state), Is.False, "a different schedule is a different state");
    }

    #endregion
}
