using MemoryPack;
using OutWit.Controller.CalculiX.Model;

namespace OutWit.Controller.CalculiX.Tests.Model;

/// <summary>
/// The result index appended to the sweep state in 0.2.0 must be invisible
/// to every reader of a PRE-0.2.0 payload: the default MemoryPack format
/// leaves an absent trailing member at its CLR default (null for a list),
/// and the first production read of a legacy sweep through the server's
/// door died on exactly that null. The model owns the guarantee, not its
/// callers.
/// </summary>
[TestFixture]
public sealed partial class SweepStateDataTests
{
    /// <summary>The 0.1.x wire shape of the state: five members, no index.</summary>
    [MemoryPackable]
    private sealed partial class LegacySweepState
    {
        [MemoryPackOrder(0)] public int ChunkIndex { get; set; }
        [MemoryPackOrder(1)] public int NextVariantOrdinal { get; set; }
        [MemoryPackOrder(2)] public int CompletedCount { get; set; }
        [MemoryPackOrder(3)] public int FailedCount { get; set; }
        [MemoryPackOrder(4)] public Guid? ManifestBlobId { get; set; }
    }

    #region Legacy Payload Tests

    [Test]
    public void LegacyPayloadReadsWithAnEmptyIndexTest()
    {
        var manifest = Guid.NewGuid();
        var legacy = MemoryPackSerializer.Serialize(new LegacySweepState
        {
            ChunkIndex = 3, NextVariantOrdinal = 7, CompletedCount = 6, FailedCount = 1, ManifestBlobId = manifest
        });

        var state = MemoryPackSerializer.Deserialize<SweepStateData>(legacy);

        Assert.That(state, Is.Not.Null);
        Assert.That(state!.ChunkIndex, Is.EqualTo(3));
        Assert.That(state.ManifestBlobId, Is.EqualTo(manifest));
        Assert.That(state.Results, Is.Not.Null, "an absent index reads as empty, never null");
        Assert.That(state.Results, Is.Empty);

        // The readers that bit in production: value comparison and cloning.
        Assert.That(state.Is(state.Clone()), Is.True);
        Assert.That(state.Clone().Results, Is.Empty);
    }

    [Test]
    public void IndexRoundTripsTest()
    {
        var state = new SweepStateData
        {
            ChunkIndex = 1,
            Results = [new SweepResultIndexEntryData { VariantIndex = 0, Succeeded = true, FrdBlobId = Guid.NewGuid(), Label = "XMAX=300" }]
        };

        var copy = MemoryPackSerializer.Deserialize<SweepStateData>(MemoryPackSerializer.Serialize(state));

        Assert.That(copy, Is.Not.Null);
        Assert.That(copy!.Is(state), Is.True);
        Assert.That(copy.Results, Has.Count.EqualTo(1));
        Assert.That(copy.Results[0].Label, Is.EqualTo("XMAX=300"));
    }

    #endregion
}
