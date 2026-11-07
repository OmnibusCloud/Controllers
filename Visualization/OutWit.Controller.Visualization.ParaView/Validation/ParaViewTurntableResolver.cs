using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// Turns an output option's camera move (the "turntable" document) into the ordered list of
/// (timestep, azimuth, elevation, dolly) outputs over the resolved timesteps (docs 03, section 27
/// item 1; docs 06, part B): a fixed data time gives every selected timestep the full move, an
/// advancing data time spreads the selected timesteps over one move. Validates the move's bounds
/// before any task is allocated. Without a move the plan is the timestep list itself, camera as
/// captured.
/// </summary>
public static class ParaViewTurntableResolver
{
    #region Constants

    /// <summary>Largest total sweep accepted, in degrees (ten full turns either way).</summary>
    public const double MAX_ABS_DEGREES = 3600.0;

    /// <summary>Largest total rise accepted, in degrees either way (a rigid rotation past the pole flips the framing).</summary>
    public const double MAX_ABS_ELEVATION_DEGREES = 170.0;

    /// <summary>Smallest end distance factor accepted.</summary>
    public const double MIN_DOLLY_FACTOR = 0.05;

    /// <summary>Largest end distance factor accepted.</summary>
    public const double MAX_DOLLY_FACTOR = 20.0;

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

        if (!double.IsFinite(turntable.Degrees))
            errors.Add($"turntable sweep must be a finite number of degrees, got {turntable.Degrees}");
        else if (Math.Abs(turntable.Degrees) > MAX_ABS_DEGREES)
            errors.Add($"turntable sweep of {turntable.Degrees} degrees exceeds {MAX_ABS_DEGREES}");

        if (!double.IsFinite(turntable.ElevationDegrees))
            errors.Add($"camera elevation must be a finite number of degrees, got {turntable.ElevationDegrees}");
        else if (Math.Abs(turntable.ElevationDegrees) > MAX_ABS_ELEVATION_DEGREES)
            errors.Add($"camera elevation of {turntable.ElevationDegrees} degrees exceeds ±{MAX_ABS_ELEVATION_DEGREES}");

        if (!double.IsFinite(turntable.DollyFactor) || turntable.DollyFactor < MIN_DOLLY_FACTOR || turntable.DollyFactor > MAX_DOLLY_FACTOR)
            errors.Add($"camera dolly factor must be between {MIN_DOLLY_FACTOR} and {MAX_DOLLY_FACTOR}, got {turntable.DollyFactor}");

        if (double.IsFinite(turntable.Degrees) && turntable.Degrees == 0.0
            && double.IsFinite(turntable.ElevationDegrees) && turntable.ElevationDegrees == 0.0
            && double.IsFinite(turntable.DollyFactor) && turntable.DollyFactor == 1.0)
            errors.Add("the camera move moves nothing: zero sweep, zero elevation and a dolly factor of 1");

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
                steps.Add(MoveAt(turntable, timestepIndices[position], i, frames));
            }

            return steps;
        }

        foreach (var index in timestepIndices)
        {
            for (var i = 0; i < frames; i++)
                steps.Add(MoveAt(turntable, index, i, frames));
        }

        return steps;
    }

    /// <summary>
    /// The camera move of output <paramref name="orbitIndex"/> of <paramref name="frames"/>: the
    /// azimuth progresses as <c>i / N</c> (cyclic: a 360° orbit loops), the elevation and the dolly
    /// as <c>i / (N - 1)</c> (the last output reaches the full move); with <see cref="ParaViewTurntableData.Oscillate"/>
    /// every component sways by <c>sin(2πi/N) / 2</c> of its total around the captured framing.
    /// </summary>
    /// <param name="turntable">The move.</param>
    /// <param name="timestepIndex">The output's timestep.</param>
    /// <param name="orbitIndex">Position in the move.</param>
    /// <param name="frames">Number of outputs of the move.</param>
    /// <returns>The output.</returns>
    public static ParaViewOrbitStep MoveAt(ParaViewTurntableData turntable, int timestepIndex, int orbitIndex, int frames)
    {
        double cyclic;
        double reaching;
        if (turntable.Oscillate)
        {
            cyclic = reaching = Math.Sin(2.0 * Math.PI * orbitIndex / frames) / 2.0;
        }
        else
        {
            cyclic = (double)orbitIndex / frames;
            reaching = frames > 1 ? (double)orbitIndex / (frames - 1) : 0.0;
        }

        var azimuth = turntable.Degrees * cyclic;
        var elevation = turntable.ElevationDegrees * reaching;
        var dolly = turntable.DollyFactor == 1.0 ? 1.0 : Math.Pow(turntable.DollyFactor, turntable.Oscillate ? 2.0 * reaching : reaching);
        return new ParaViewOrbitStep(timestepIndex, orbitIndex, azimuth, elevation, dolly);
    }

    #endregion
}
