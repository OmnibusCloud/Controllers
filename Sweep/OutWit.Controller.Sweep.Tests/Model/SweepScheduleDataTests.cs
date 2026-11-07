using MemoryPack;
using OutWit.Controller.Sweep.Model;

namespace OutWit.Controller.Sweep.Tests.Model;

/// <summary>
/// The schedule is its own wire type beside the state - the state keeps the
/// seven members every released reader speaks (frozen by the wire-layout
/// guard), and the schedule round-trips, compares and clones on its own.
/// </summary>
[TestFixture]
public sealed class SweepScheduleDataTests
{
    #region Round Trip Tests

    [Test]
    public void TheScheduleRoundTripsAndTakesPartInEqualityTest()
    {
        var schedule = new SweepScheduleData { ChunkSizes = [3, 3, 1] };

        var read = MemoryPackSerializer.Deserialize<SweepScheduleData>(MemoryPackSerializer.Serialize(schedule));

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }));
        Assert.That(read.Is(schedule), Is.True);
        Assert.That(read.ToString(), Is.EqualTo("schedule: 3 chunk(s) [3, 3, 1]"));

        var other = schedule.Clone();
        Assert.That(other.Is(schedule), Is.True);
        other.ChunkSizes = [2, 2, 2, 1];
        Assert.That(other.Is(schedule), Is.False, "a different schedule is a different value");
        Assert.That(schedule.ChunkSizes, Is.EqualTo(new[] { 3, 3, 1 }), "the clone owns its list");
    }

    [Test]
    public void TheStateStillSpeaksTheShippedSevenMembersTest()
    {
        // A state written by a Sweep 2.0 - 2.1 host and one written by 2.2 are the same bytes
        // for the same values: the schedule never entered the state's layout.
        var state = new SweepStateData { ChunkIndex = 2, NextVariantOrdinal = 5, SucceededCount = 4, FailedCount = 1, ManifestBlobId = Guid.NewGuid() };

        var read = MemoryPackSerializer.Deserialize<SweepStateData>(MemoryPackSerializer.Serialize(state));

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.Is(state), Is.True);
        Assert.That(typeof(SweepStateData).GetProperties().Count(property => property.GetCustomAttributes(typeof(MemoryPackOrderAttribute), false).Length > 0), Is.EqualTo(7));
    }

    #endregion
}
