namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// A run's scratch in the host's temp folder, by both of its names: the scope
/// the temp folder handed out, which is what goes back to it for deletion, and
/// the form of the same directory OpenFOAM is given - the scope itself, or its
/// 8.3 short form on Windows when the scope's path carries whitespace.
/// </summary>
public sealed class FoamScratch
{
    #region Constructors

    /// <summary>
    /// Names a scratch.
    /// </summary>
    /// <param name="scopePath">The scope as the host's temp folder returned it.</param>
    /// <param name="usablePath">The same directory at a path OpenFOAM can use.</param>
    public FoamScratch(string scopePath, string usablePath)
    {
        ScopePath = scopePath;
        UsablePath = usablePath;
    }

    #endregion

    #region Properties

    /// <summary>The scope as the host's temp folder returned it; the path its deletion takes.</summary>
    public string ScopePath { get; }

    /// <summary>The same directory at a path without whitespace; every file of the run lives under it.</summary>
    public string UsablePath { get; }

    #endregion
}
