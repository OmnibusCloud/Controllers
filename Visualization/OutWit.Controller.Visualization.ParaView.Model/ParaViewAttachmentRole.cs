namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Semantic role of a package attachment. The role drives per-task attachment subsetting:
/// a series index file rides in every task's subset, while reader inputs ride only in the
/// subsets of the timesteps they belong to.
/// </summary>
public enum ParaViewAttachmentRole
{
    /// <summary>
    /// A data file a reader in the state opens (a mesh, a field, one piece of a file series).
    /// </summary>
    ReaderInput = 0,

    /// <summary>
    /// A collection/index file (for example a .pvd or a .series JSON) whose reader needs the
    /// whole index at load time; present in every task's subset while the referenced pieces
    /// stay per-timestep.
    /// </summary>
    SeriesIndex = 1,

    /// <summary>
    /// A file the state references that is not read through a reader input (textures, lookup
    /// table presets, fonts); static, present in every task's subset.
    /// </summary>
    Auxiliary = 2
}
