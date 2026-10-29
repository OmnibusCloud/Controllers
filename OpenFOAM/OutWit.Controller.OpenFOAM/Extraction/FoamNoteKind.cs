namespace OutWit.Controller.OpenFOAM.Extraction;

/// <summary>
/// How much a note changes the reading of the variant.
/// </summary>
public enum FoamNoteKind
{
    /// <summary>The values stand; the note says how they were obtained or what the run did besides.</summary>
    Info = 0,

    /// <summary>A column is not reported, or a swept value does not do what the study meant; the note says why.</summary>
    Warning = 1,

    /// <summary>Extraction failed: the response row is not to be trusted as a result.</summary>
    Error = 2
}
