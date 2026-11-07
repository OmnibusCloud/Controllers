namespace OutWit.Controller.Visualization.ParaView.Validation;

/// <summary>
/// One output of a turntable plan: the timestep to select and the camera azimuth to apply, in
/// render order (<see cref="OrbitIndex"/> counts the orbit outputs of one data time, or of the
/// whole job when the data time advances).
/// </summary>
/// <param name="TimestepIndex">Index into the resolved timeline.</param>
/// <param name="OrbitIndex">Position of the output in its orbit, 0-based.</param>
/// <param name="AzimuthDegrees">Camera azimuth about the orbit axis, degrees.</param>
public readonly record struct ParaViewOrbitStep(int TimestepIndex, int OrbitIndex, double AzimuthDegrees, double ElevationDegrees = 0.0, double DollyFactor = 1.0);
