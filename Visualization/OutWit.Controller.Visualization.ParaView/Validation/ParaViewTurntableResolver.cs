using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Turns an output option's turntable into the ordered list of (timestep, azimuth) outputs over the
/// resolved timesteps (docs 03, section 27 item 1): a fixed data time gives every selected timestep
/// a full orbit, an advancing data time spreads the selected timesteps over one orbit. Validates
/// the turntable's bounds before any task is allocated. Without a turntable the plan is the
/// timestep list itself, azimuth 0.
/// </summary>
public static class ParaViewTurntableResolver
{
    #region Constants

    /// <summary>Largest total sweep accepted, in degrees (ten full turns either way).</summary>
    public const double MAX_ABS_DEGREES = 3600.0;

    #endregion

    #region Functions

    /// <summary>
    /// Validates the turntable options (null is valid: no orbit).
    /// </summary>
    /// <param name="turntable">The turntable or null.</param>
    /// <param name="errors">Receives permanent failures.</param>
    public static void Validate(ParaViewTurntableData? turntable, ICollection<string> errors)
    {
        if (turntable == null)
            return;

        if (turntable.Frames < 1)
            errors.Add($"turntable must render at least 1 orbit frame, got {turntable.Frames}");
        else if (turntable.Frames > ParaViewInputLimits.MAX_OUTPUTS)
            errors.Add($"turntable requests {turntable.Frames} orbit frames, over the {ParaViewInputLimits.MAX_OUTPUTS} outputs per job limit");

        if (double.IsNaN(turntable.Degrees) || double.IsInfinity(turntable.Degrees) || turntable.Degrees == 0.0)
            errors.Add($"turntable sweep must be a non-zero number of degrees, got {turntable.Degrees}");
        else if (Math.Abs(turntable.Degrees) > MAX_ABS_DEGREES)
            errors.Add($"turntable sweep of {turntable.Degrees} degrees exceeds {MAX_ABS_DEGREES}");

        if (!Enum.IsDefined(turntable.TimeMode))
            errors.Add($"unknown turntable time mode {turntable.TimeMode}");

        if (!Enum.IsDefined(turntable.Axis))
            errors.Add($"unknown turntable axis {turntable.Axis}");
    }

    /// <summary>
    /// Number of outputs the plan will hold for a count of selected timesteps.
    /// </summary>
    /// <param name="timestepCount">Number of selected timesteps.</param>
    /// <param name="turntable">The turntable or null.</param>
    /// <returns>The output count (0 when no timestep is selected).</returns>
    public static long CountOutputs(int timestepCount, ParaViewTurntableData? turntable)
    {
        if (timestepCount <= 0)
            return 0;

        if (turntable == null)
            return timestepCount;

        var frames = Math.Max(0, turntable.Frames);
        return turntable.TimeMode == ParaViewTurntableTimeMode.Advancing
            ? frames
            : (long)timestepCount * frames;
    }

    /// <summary>
    /// Resolves the plan. Call after <see cref="Validate"/> succeeded.
    /// </summary>
    /// <param name="timestepIndices">The selected timesteps in render order.</param>
    /// <param name="turntable">The turntable or null.</param>
    /// <returns>The outputs in render order.</returns>
    public static IReadOnlyList<ParaViewOrbitStep> Resolve(IReadOnlyList<int> timestepIndices, ParaViewTurntableData? turntable)
    {
        if (timestepIndices.Count == 0)
            return [];

        if (turntable == null)
            return [.. timestepIndices.Select(index => new ParaViewOrbitStep(index, 0, 0.0))];

        var frames = Math.Max(1, turntable.Frames);
        var steps = new List<ParaViewOrbitStep>((int)Math.Min(int.MaxValue, CountOutputs(timestepIndices.Count, turntable)));

        if (turntable.TimeMode == ParaViewTurntableTimeMode.Advancing)
        {
            var last = timestepIndices.Count - 1;
            for (var i = 0; i < frames; i++)
            {
                var position = frames > 1
                    ? (int)Math.Round(i * (double)last / (frames - 1), MidpointRounding.AwayFromZero)
                    : 0;
                steps.Add(new ParaViewOrbitStep(timestepIndices[position], i, Azimuth(turntable, i, frames)));
            }

            return steps;
        }

        foreach (var index in timestepIndices)
        {
            for (var i = 0; i < frames; i++)
                steps.Add(new ParaViewOrbitStep(index, i, Azimuth(turntable, i, frames)));
        }

        return steps;
    }

    #endregion

    #region Tools

    private static double Azimuth(ParaViewTurntableData turntable, int orbitIndex, int frames)
    {
        return turntable.Degrees * orbitIndex / frames;
    }

    #endregion
}
