using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// The chunk schedule a sweep runs by - the planned size of every chunk,
/// summing to the variant count, as <c>Sweep.Plan</c> computed it for the run
/// (the client's bounds widened to the fleet). Set once by
/// <c>Sweep.InitState</c> into the <c>schedule</c> job variable and never
/// reassigned: a client mirrors the server's plan from it instead of
/// recomputing a schedule whose fleet width it does not know, and a run
/// resumed with it keeps the boundaries its manifest was harvested by. Its
/// own type and its own variable, so <see cref="SweepStateData"/> keeps the
/// wire layout every released reader speaks; a reader that never asks for
/// the schedule never sees it. Document clients read it as
/// <c>sweep.schedule@1</c>.
/// </summary>
[MemoryPackable]
[JobDocumentContract("sweep.schedule@1")]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepScheduleData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepScheduleData schedule)
            return false;

        return ChunkSizes.Is(schedule.ChunkSizes);
    }

    public override SweepScheduleData Clone()
    {
        return new SweepScheduleData
        {
            ChunkSizes = [.. ChunkSizes]
        };
    }

    public override string ToString()
    {
        return $"schedule: {ChunkSizes.Count} chunk(s) [{string.Join(", ", ChunkSizes)}]";
    }

    #endregion

    #region Properties

    /// <summary>Planned size of every chunk, in run order; sums to the variant count. A client reads the current chunk as <c>ChunkSizes[state.ChunkIndex]</c>.</summary>
    [MemoryPackOrder(0)]
    public List<int> ChunkSizes { get; set; } = [];

    #endregion
}
