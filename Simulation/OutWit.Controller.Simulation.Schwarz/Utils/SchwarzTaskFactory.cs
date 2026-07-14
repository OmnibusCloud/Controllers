using System.Linq;
using OutWit.Controller.Simulation.Model;

namespace OutWit.Controller.Simulation.Schwarz.Utils;

internal static class SchwarzTaskFactory
{
    #region Functions

    public static List<SchwarzTaskData?> BuildTasks(SchwarzPlanData plan, SchwarzRoundData state, bool emitField)
    {
        var tasks = new List<SchwarzTaskData?>(plan.Parts);

        for (var i = 0; i < plan.Parts; i++)
        {
            var boundarySet = state.Boundaries.FirstOrDefault(set => set.SubdomainIndex == i);

            // Round 0 legitimately has empty sets (zero band); after that a missing
            // set means a malformed state — solving on a zero band mid-iteration
            // would silently perturb the iterate instead of failing.
            if (boundarySet == null && state.Round > 0)
                throw new InvalidOperationException($"Schwarz task factory: state at round {state.Round} carries no boundary set for subdomain {i}.");

            var boundaries = boundarySet?.BlobIds ?? [];

            tasks.Add(new SchwarzTaskData
            {
                SubdomainIndex = i,
                SubdomainBlobId = plan.SubdomainBlobIds[i],
                Dof = plan.Dof,
                Round = state.Round,
                BoundaryInBlobIds = [.. boundaries],
                EmitField = emitField
            });
        }

        return tasks;
    }

    #endregion
}
