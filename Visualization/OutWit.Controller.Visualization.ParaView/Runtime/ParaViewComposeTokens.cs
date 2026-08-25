using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The wire tokens of the composer task: the data-scene enums as the strings <c>compose_scene.py</c>
/// switches on. Kept in one place so the C# side and the script cannot drift apart silently.
/// </summary>
public static class ParaViewComposeTokens
{
    #region Constants

    /// <summary>Point-array association.</summary>
    public const string POINTS = "POINTS";

    /// <summary>Cell-array association.</summary>
    public const string CELLS = "CELLS";

    /// <summary>Shaded surface.</summary>
    public const string SURFACE = "Surface";

    /// <summary>Shaded surface with edges.</summary>
    public const string SURFACE_WITH_EDGES = "Surface With Edges";

    /// <summary>Wireframe.</summary>
    public const string WIREFRAME = "Wireframe";

    /// <summary>Isometric camera direction.</summary>
    public const string ISOMETRIC = "isometric";

    /// <summary>Fit the union of all timesteps.</summary>
    public const string FIT_ALL = "all";

    /// <summary>Fit the last timestep.</summary>
    public const string FIT_LAST = "last";

    /// <summary>Fit the first timestep.</summary>
    public const string FIT_FIRST = "first";

    #endregion

    #region Functions

    /// <summary>
    /// The association token of a colour association.
    /// </summary>
    /// <param name="association">The association.</param>
    /// <returns>"POINTS" or "CELLS".</returns>
    public static string WireToken(ParaViewColorAssociation association)
    {
        return association == ParaViewColorAssociation.Cells ? CELLS : POINTS;
    }

    /// <summary>
    /// The ParaView representation type text of a representation.
    /// </summary>
    /// <param name="representation">The representation.</param>
    /// <returns>The representation type text.</returns>
    public static string WireToken(ParaViewSceneRepresentation representation)
    {
        return representation switch
        {
            ParaViewSceneRepresentation.SurfaceWithEdges => SURFACE_WITH_EDGES,
            ParaViewSceneRepresentation.Wireframe => WIREFRAME,
            _ => SURFACE
        };
    }

    /// <summary>
    /// The camera direction token of a direction.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>"isometric" or a signed axis ("+x" … "-z").</returns>
    public static string WireToken(ParaViewCameraDirection direction)
    {
        return direction switch
        {
            ParaViewCameraDirection.PlusX => "+x",
            ParaViewCameraDirection.MinusX => "-x",
            ParaViewCameraDirection.PlusY => "+y",
            ParaViewCameraDirection.MinusY => "-y",
            ParaViewCameraDirection.PlusZ => "+z",
            ParaViewCameraDirection.MinusZ => "-z",
            _ => ISOMETRIC
        };
    }

    /// <summary>
    /// The fit token of a camera fit.
    /// </summary>
    /// <param name="fit">The fit.</param>
    /// <returns>"all", "last" or "first".</returns>
    public static string WireToken(ParaViewCameraFit fit)
    {
        return fit switch
        {
            ParaViewCameraFit.LastTimestep => FIT_LAST,
            ParaViewCameraFit.FirstTimestep => FIT_FIRST,
            _ => FIT_ALL
        };
    }

    #endregion
}
