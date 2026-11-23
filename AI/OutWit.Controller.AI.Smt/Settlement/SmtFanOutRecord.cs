namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// One fan-out of a run in numbers: what it was asked for and what came back, counted in
/// packages of problems. It names no package and no machine.
/// </summary>
/// <param name="Round">The fan-out: main, repair or dispute.</param>
/// <param name="Packages">The packages it gave out.</param>
/// <param name="Problems">The problems those packages hold.</param>
/// <param name="Copies">The answers a selected package was asked for: two where a share is sampled, three in a dispute, one otherwise.</param>
/// <param name="Selected">The packages asked for that many answers; null when the script did not state what it asked for.</param>
/// <param name="Complete">The packages that came back with that many taken answers, or more.</param>
/// <param name="Unanswered">The packages no taken answer came back for.</param>
internal sealed record SmtFanOutRecord(string Round, int Packages, long Problems, int Copies, int? Selected, int Complete, int Unanswered)
{
    /// <summary>Whether fewer packages came back with all their answers than were asked for them.</summary>
    public bool IsShort => Selected is { } selected && Complete < selected;
}
