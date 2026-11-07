namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// The solver family a study runs on: which block of the options carries the
/// study's input, which node activity the family's script fans out to, and
/// which result a manifest row carries. A new family is a new member here, a
/// new block in <see cref="SweepOptionsData"/> and a new result member in
/// <see cref="SweepManifestRowData"/> - appended, never inserted.
/// </summary>
public enum SweepFamily
{
    /// <summary>CalculiX decks, solved whole on a node by <c>Ccx.Solve</c>.</summary>
    CalculiX = 0,

    /// <summary>OpenFOAM cases, run whole on a node by <c>Foam.Run</c>.</summary>
    OpenFOAM = 1
}
