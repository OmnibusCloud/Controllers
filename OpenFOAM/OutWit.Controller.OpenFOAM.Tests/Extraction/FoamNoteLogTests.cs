using OutWit.Controller.OpenFOAM.Extraction;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

/// <summary>
/// The notes of a variant inside its log tail: the CalculiX controller's
/// grammar, so one reader in the app serves both families.
/// </summary>
[TestFixture]
public class FoamNoteLogTests
{
    #region Constants

    private const string SOLVER_TAIL = "Time = 1\nsmoothSolver:  Solving for Ux, Initial residual = 1\n--> FOAM FATAL ERROR: (fake) the solve diverged\n";

    #endregion

    #region Tools

    private static FoamNote Note(FoamNoteKind kind, string? column, string text)
    {
        return new FoamNote { Kind = kind, Column = column, Text = text };
    }

    #endregion

    #region Compose Tests

    [Test]
    public void ATailWithoutNotesIsKeptAsItIsTest()
    {
        Assert.That(FoamNoteLog.Compose(null, []), Is.Null, "a successful run without notes: no tail");
        Assert.That(FoamNoteLog.Compose(string.Empty, []), Is.Empty);
        Assert.That(FoamNoteLog.Compose(SOLVER_TAIL, []), Is.EqualTo(SOLVER_TAIL), "byte for byte, its last line break included");
    }

    [Test]
    public void TheNotesFollowTheSolverTailAfterTheSeparatorTest()
    {
        var composed = FoamNoteLog.Compose(SOLVER_TAIL,
        [
            Note(FoamNoteKind.Info, null, "The task asks for 2 ranks."),
            Note(FoamNoteKind.Warning, "wake.U@probe1(9 9 9)_x", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported.")
        ]);

        Assert.That(composed, Is.EqualTo(
            "Time = 1\nsmoothSolver:  Solving for Ux, Initial residual = 1\n--> FOAM FATAL ERROR: (fake) the solve diverged\n" +
            "--- notes ---\n" +
            "[note]\tINFO\t\tThe task asks for 2 ranks.\n" +
            "[note]\tWARNING\twake.U@probe1(9 9 9)_x\twake: probe 1 (9 9 9) of U is outside the mesh - not reported."));
    }

    [Test]
    public void ASuccessfulRunsTailStartsWithTheSeparatorTest()
    {
        var composed = FoamNoteLog.Compose(null, [Note(FoamNoteKind.Error, null, "response extraction failed")]);

        Assert.That(composed, Is.EqualTo("--- notes ---\n[note]\tERROR\t\tresponse extraction failed"));
    }

    [Test]
    public void TabsAndLineBreaksInsideAFieldBecomeSpacesTest()
    {
        var line = FoamNoteLog.Line(Note(FoamNoteKind.Warning, "a\tb", "first\nsecond\r\nthird\tfourth"));

        Assert.That(line, Is.EqualTo("[note]\tWARNING\ta b\tfirst second  third fourth"));
        Assert.That(line.Split('\t'), Has.Length.EqualTo(4), "four fields, whatever the note says");
    }

    #endregion

    #region Parse Tests

    [Test]
    public void ComposedNotesReadBackAsTheyWereTest()
    {
        var notes = new[]
        {
            Note(FoamNoteKind.Info, null, "Response 'coeffs': lRef follows the case's coeffs (system/controlDict:6), which the study sweeps - 10 in this variant."),
            Note(FoamNoteKind.Warning, "coeffs.Cd", "coeffs: the last row of coefficient.dat is at time 3, the run ended at 4 - not reported."),
            Note(FoamNoteKind.Warning, "probes.U@probe1(9 9 9)_z", "probes: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            Note(FoamNoteKind.Error, null, "response extraction failed")
        };

        foreach (var tail in new[] { SOLVER_TAIL, null })
        {
            var (solverTail, parsed) = FoamNoteLog.Parse(FoamNoteLog.Compose(tail, notes));

            Assert.That(solverTail, Is.EqualTo(tail?.TrimEnd('\n')), "the solver's tail, its last line break aside; none for a successful run");
            Assert.That(parsed, Has.Count.EqualTo(notes.Length));
            for (var index = 0; index < notes.Length; index++)
                Assert.That(parsed[index].Is(notes[index]), Is.True, $"{parsed[index]} read back as {notes[index]}");
        }
    }

    [Test]
    public void AFieldWithATabReadsBackCleanedTest()
    {
        var (_, parsed) = FoamNoteLog.Parse(FoamNoteLog.Compose(null, [Note(FoamNoteKind.Warning, "a\tb", "x\ty\nz")]));

        Assert.That(parsed.Single().Column, Is.EqualTo("a b"));
        Assert.That(parsed.Single().Text, Is.EqualTo("x y z"));
    }

    [Test]
    public void ATailWithoutTheSeparatorCarriesNoNotesTest()
    {
        AssertParsed(SOLVER_TAIL, SOLVER_TAIL);
        AssertParsed("[note]\tINFO\t\tlooks like a note", "[note]\tINFO\t\tlooks like a note");
        AssertParsed(null, null);
        AssertParsed(string.Empty, null);

        static void AssertParsed(string? logTail, string? solverTail)
        {
            var (tail, notes) = FoamNoteLog.Parse(logTail);
            Assert.That(tail, Is.EqualTo(solverTail), "the tail whole");
            Assert.That(notes, Is.Empty, "a note line counts after the separator only");
        }
    }

    [Test]
    public void TheCalculiXExampleReadsTheSameTest()
    {
        // The example the CalculiX controller documents, a note line with a
        // Windows line break among them: one grammar for both families.
        var (tail, notes) = FoamNoteLog.Parse(
            "--- notes ---\n" +
            "[note]\tWARNING\t\tSOLVER=PASTIX is not linked in the bundled ccx of this node: the parameter was removed from *STATIC and the solve used PARDISO\r\n" +
            "[note]\tINFO\trf_sum_z@fix\t9 of 9 rows were printed in the local frame of *TRANSFORM, NSET=FIX and rotated to the global frame\r\n" +
            "not a note line\n" +
            "[note]\tNOTICE\t\tan unknown kind reads as INFO");

        Assert.That(tail, Is.Null);
        Assert.That(notes.Select(note => (note.Kind, note.Column)), Is.EqualTo(new[]
        {
            (FoamNoteKind.Warning, (string?)null), (FoamNoteKind.Info, (string?)"rf_sum_z@fix"), (FoamNoteKind.Info, (string?)null)
        }));
        Assert.That(notes[1].Text, Does.EndWith("rotated to the global frame"));
    }

    #endregion
}
