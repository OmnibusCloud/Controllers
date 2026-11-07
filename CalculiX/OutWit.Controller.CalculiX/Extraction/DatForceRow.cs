namespace OutWit.Controller.CalculiX.Extraction;

/// <summary>
/// One row of a "forces (fx,fy,fz) for set …" block of a ccx .dat file: the
/// node, its three reaction-force components, and whether ccx printed them
/// in the node's local <c>*TRANSFORM</c> frame (the trailing <c>L</c> of the
/// row; <c>*NODE PRINT</c> prints local by default) rather than the global one.
/// </summary>
/// <param name="Node">Node id.</param>
/// <param name="Fx">First component.</param>
/// <param name="Fy">Second component.</param>
/// <param name="Fz">Third component.</param>
/// <param name="Local">True when the components are in the node's local frame.</param>
public readonly record struct DatForceRow(int Node, double Fx, double Fy, double Fz, bool Local);
