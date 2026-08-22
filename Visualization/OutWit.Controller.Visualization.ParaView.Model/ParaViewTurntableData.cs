using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// A camera orbit ("turntable") as an output option: the controller renders <see cref="Frames"/>
/// outputs with the state's camera revolved about the focal point by <see cref="Degrees"/> in
/// total, at a fixed or an advancing data time (<see cref="TimeMode"/>). The orbit is an
/// output-side transform of the captured camera — no animation track in the state is needed,
/// one timestep in gives a showcase sequence out. The azimuth of output <c>i</c> of <c>N</c> is
/// <c>Degrees * i / N</c> (the last output stops short of the first, so a 360° orbit loops).
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
               && Axis.Is(other.Axis);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewTurntableData
        {
            Frames = Frames,
            Degrees = Degrees,
            TimeMode = TimeMode,
            Axis = Axis
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
    /// moves counter-clockwise seen from the orbit axis). Non-zero, at most ten full turns.
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

    #endregion
}
