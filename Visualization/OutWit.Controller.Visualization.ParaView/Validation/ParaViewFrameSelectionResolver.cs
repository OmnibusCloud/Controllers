using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Turns a frame selection into the ordered list of timestep indices to render against a resolved
/// timeline, rejecting indices outside it and selections larger than the output limit before any
/// task is allocated. A static scene has exactly one timestep, index 0.
/// </summary>
public static class ParaViewFrameSelectionResolver
{
    #region Functions

    /// <summary>
    /// Resolves the selection.
    /// </summary>
    /// <param name="selection">The frame selection.</param>
    /// <param name="timestepCount">Number of timesteps in the resolved timeline (1 for a static scene).</param>
    /// <param name="errors">Receives permanent failures.</param>
    /// <returns>The timestep indices in render order (empty when an error was recorded).</returns>
    public static IReadOnlyList<int> Resolve(ParaViewFrameSelectionData selection, int timestepCount, ICollection<string> errors)
    {
        var count = Math.Max(1, timestepCount);
        var indices = new List<int>();

        switch (selection.Mode)
        {
            case ParaViewFrameSelectionMode.Single:
                indices.Add(selection.First);
                break;

            case ParaViewFrameSelectionMode.Range:
                if (selection.Step < 1)
                {
                    errors.Add($"frame selection step must be at least 1, got {selection.Step}");
                    return [];
                }

                if (selection.Last < selection.First)
                {
                    errors.Add($"frame selection range is empty: last ({selection.Last}) is before first ({selection.First})");
                    return [];
                }

                if (((long)selection.Last - selection.First) / selection.Step + 1 > ParaViewInputLimits.MAX_OUTPUTS)
                {
                    errors.Add($"frame selection requests more than {ParaViewInputLimits.MAX_OUTPUTS} outputs");
                    return [];
                }

                if (selection.First < 0 || selection.Last >= count)
                {
                    errors.Add($"frame selection range {selection.First}..{selection.Last} is outside the timeline of {count} timestep(s)");
                    return [];
                }

                for (long index = selection.First; index <= selection.Last; index += selection.Step)
                    indices.Add((int)index);
                break;

            case ParaViewFrameSelectionMode.All:
                if (count > ParaViewInputLimits.MAX_OUTPUTS)
                {
                    errors.Add($"the timeline has {count} timesteps, over the {ParaViewInputLimits.MAX_OUTPUTS} outputs per job limit");
                    return [];
                }

                indices.AddRange(Enumerable.Range(0, count));
                break;

            case ParaViewFrameSelectionMode.Explicit:
                if (selection.Indices.Count == 0)
                {
                    errors.Add("explicit frame selection lists no timestep indices");
                    return [];
                }

                if (selection.Indices.Count > ParaViewInputLimits.MAX_OUTPUTS)
                {
                    errors.Add($"explicit frame selection lists more than {ParaViewInputLimits.MAX_OUTPUTS} outputs");
                    return [];
                }

                if (selection.Indices.Distinct().Count() != selection.Indices.Count)
                {
                    errors.Add("explicit frame selection repeats a timestep index");
                    return [];
                }

                indices.AddRange(selection.Indices);
                break;

            default:
                errors.Add($"unknown frame selection mode {selection.Mode}");
                return [];
        }

        var outOfRange = indices.Where(index => index < 0 || index >= count).ToList();
        if (outOfRange.Count > 0)
        {
            errors.Add($"frame selection references timestep indices outside the timeline of {count} timestep(s): {string.Join(", ", outOfRange.Take(8))}{(outOfRange.Count > 8 ? ", …" : string.Empty)}");
            return [];
        }

        return indices;
    }

    #endregion
}
