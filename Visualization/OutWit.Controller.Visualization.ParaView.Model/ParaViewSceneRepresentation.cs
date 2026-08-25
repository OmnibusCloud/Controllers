namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// How a composed scene draws its data (ParaView representation types).
/// </summary>
public enum ParaViewSceneRepresentation
{
    /// <summary>Shaded surface.</summary>
    Surface = 0,

    /// <summary>Shaded surface with the mesh edges drawn.</summary>
    SurfaceWithEdges = 1,

    /// <summary>Edges only.</summary>
    Wireframe = 2
}
