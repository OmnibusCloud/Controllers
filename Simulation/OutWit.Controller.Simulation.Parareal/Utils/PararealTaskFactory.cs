using OutWit.Math.Simulation;

namespace OutWit.Controller.Simulation.Parareal.Utils;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math
using OutWit.Math.Simulation.Model.Parareal;

internal static class PararealTaskFactory
{
    #region Functions

    public static List<PararealTaskData?> BuildTasks(PararealPlanData plan, PararealStateData state, bool emitSnapshots)
    {
        // Frontier: after k iterations slabs 0..k−1 are exact and leave the
        // wave. The snapshot wave recomputes every slab from converged
        // states, so it starts at 0.
        var frontier = emitSnapshots ? 0 : Math.Min(state.Round, plan.Slabs - 1);

        // The canonical fine δt — the SAME expression (and bitwise value) the
        // kernel uses; SlabBoundaries[^1] is pinned to TotalTime at slice time.
        var timeStep = plan.SlabBoundaries[^1] / (plan.Slabs * plan.FineStepsPerSlab);

        var tasks = new List<PararealTaskData?>(plan.Slabs - frontier);
        for (var slab = frontier; slab < plan.Slabs; slab++)
        {
            tasks.Add(new PararealTaskData
            {
                TimeStep = timeStep,
                SlabIndex = slab,
                ModelBlobId = plan.ModelBlobId,
                StateInBlobId = state.StateBlobIds[slab],
                SlabStart = plan.SlabBoundaries[slab],
                SlabEnd = plan.SlabBoundaries[slab + 1],
                Steps = plan.FineStepsPerSlab,
                Dof = plan.Dof,
                EmitSnapshots = emitSnapshots,
                SnapshotsPerSlab = plan.SnapshotsPerSlab
            });
        }

        return tasks;
    }

    #endregion
}
