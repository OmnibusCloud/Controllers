namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// The direction a composed scene's camera looks from before it is fitted to the data (the
/// standard view buttons of the GUI). The camera move of the output options (turntable) revolves
/// this framing at render time.
/// </summary>
public enum ParaViewCameraDirection
{
    /// <summary>From the (+1, +1, +1) octant, Z up — the classic engineering isometric.</summary>
    Isometric = 0,

    /// <summary>Looking along -X from +X.</summary>
    PlusX = 1,

    /// <summary>Looking along +X from -X.</summary>
    MinusX = 2,

    /// <summary>Looking along -Y from +Y.</summary>
    PlusY = 3,

    /// <summary>Looking along +Y from -Y.</summary>
    MinusY = 4,

    /// <summary>Looking along -Z from +Z (top view).</summary>
    PlusZ = 5,

    /// <summary>Looking along +Z from -Z (bottom view).</summary>
    MinusZ = 6
}
