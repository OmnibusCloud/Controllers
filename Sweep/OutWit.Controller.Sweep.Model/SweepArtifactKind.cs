namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// What an artifact of a variant is, so a document client knows what it is
/// about to download and how to open it.
/// </summary>
public enum SweepArtifactKind
{
    /// <summary>A CalculiX result file (<c>.frd</c>): fields for ParaView or PrePoMax.</summary>
    CalculiXFrd = 0,

    /// <summary>A CalculiX printed-output file (<c>.dat</c>): the requested node and element prints.</summary>
    CalculiXDat = 1,

    /// <summary>An OpenFOAM case as a zip: the case skeleton, the times and logs the artifact policy asked for, and a <c>case.foam</c> stub to open it with.</summary>
    OpenFOAMCase = 2
}
