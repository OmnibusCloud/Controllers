namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Where the array a composed scene colours by lives.
/// </summary>
public enum ParaViewColorAssociation
{
    /// <summary>A point (nodal) array — the CalculiX result blocks (NDTEMP, DISP, STRESS, ...).</summary>
    Points = 0,

    /// <summary>A cell (element) array.</summary>
    Cells = 1
}
