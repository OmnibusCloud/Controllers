using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// A camera move as an output option — the turntable of controller 0.2.0 generalised in 0.4.0
/// (docs 06, part B): the controller renders <see cref="Frames"/> outputs with the state's camera
/// moved about the focal point — revolved by <see cref="Degrees"/> in total about the orbit axis,
/// raised by <see cref="ElevationDegrees"/> in total about the camera's right axis, and moved
/// toward or away from the focal point until its distance is <see cref="DollyFactor"/> times the
/// start — at a fixed or an advancing data time (<see cref="TimeMode"/>). Every move is an
/// output-side transform of the captured camera: no animation track in the state, one timestep
/// in gives a showcase sequence out. The azimuth of output <c>i</c> of <c>N</c> is
/// <c>Degrees * i / N</c> (the last output stops short of the first, so a 360° orbit loops);
/// elevation and dolly progress as <c>i / (N - 1)</c> so the last output reaches the full move.
/// With <see cref="Oscillate"/> every component sways instead: <c>sin(2πi/N) / 2</c> of its total,
/// back and forth around the captured framing, a seamless loop. The presets of a client (orbit,
/// rise, spiral, approach, rock) are combinations of these members.
/// </summary>
[JobDocumentContract("paraview.turntable@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewTurntableData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewTurntableData other)
            return false;

        return Frames.Is(other.Frames)
               && Degrees.Is(other.Degrees, tolerance)
               && TimeMode.Is(other.TimeMode)
               && Axis.Is(other.Axis)
               && ElevationDegrees.Is(other.ElevationDegrees, tolerance)
               && DollyFactor.Is(other.DollyFactor, tolerance)
               && Oscillate.Is(other.Oscillate);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewTurntableData
        {
            Frames = Frames,
            Degrees = Degrees,
            TimeMode = TimeMode,
            Axis = Axis,
            ElevationDegrees = ElevationDegrees,
            DollyFactor = DollyFactor,
            Oscillate = Oscillate
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Number of orbit outputs (at least 1; bounded by the per-job output limit together with the
    /// selected timesteps).
    /// </summary>
    [MemoryPackOrder(0)]
    public int Frames { get; set; } = 72;

    /// <summary>
    /// Total sweep of the orbit in degrees; the sign sets the direction (positive = the camera
    /// moves counter-clockwise seen from the orbit axis). Zero when the move has no orbit (a pure
    /// rise or approach); at most ten full turns either way.
    /// </summary>
    [MemoryPackOrder(1)]
    public double Degrees { get; set; } = 360.0;

    /// <summary>
    /// How the orbit composes with the selected timesteps.
    /// </summary>
    [MemoryPackOrder(2)]
    public ParaViewTurntableTimeMode TimeMode { get; set; } = ParaViewTurntableTimeMode.Fixed;

    /// <summary>
    /// The orbit axis through the focal point.
    /// </summary>
    [MemoryPackOrder(3)]
    public ParaViewTurntableAxis Axis { get; set; } = ParaViewTurntableAxis.ViewUp;

    /// <summary>
    /// Total rise of the camera in degrees about its own right axis through the focal point
    /// (positive = the camera climbs toward the view-up, toward a top-down view; negative = it
    /// dives). 0 keeps the captured height; at most ±170 so the move never crosses the pole.
    /// Appended in Model 0.4.0.
    /// </summary>
    [MemoryPackOrder(4)]
    public double ElevationDegrees { get; set; }

    /// <summary>
    /// The camera's distance to the focal point at the end of the move, relative to the captured
    /// distance: 0.5 halves it (an approach), 2 doubles it (a retreat), 1 keeps it. Between 0.05
    /// and 20. Appended in Model 0.4.0.
    /// </summary>
    [MemoryPackOrder(5)]
    public double DollyFactor { get; set; } = 1.0;

    /// <summary>
    /// Whether the move sways back and forth around the captured framing (a seamless rocking loop:
    /// every component follows <c>sin(2πi/N) / 2</c> of its total) instead of progressing from
    /// the captured framing to the full move. Appended in Model 0.4.0.
    /// </summary>
    [MemoryPackOrder(6)]
    public bool Oscillate { get; set; }

    #endregion
}
