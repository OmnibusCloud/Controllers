using OutWit.Controller.CalculiX.Extraction;

namespace OutWit.Controller.CalculiX.Tests.Extraction;

/// <summary>
/// The notes ride inside the result's log tail, after the solver's own
/// tail and a separator line, one tab-separated line per note - the shipped
/// wire layout of the result does not change, an old client showing the
/// tail of a failed row still shows the notes, a new client parses them.
/// </summary>
[TestFixture]
public class CcxNoteLogTests
{
    #region Compose Tests

    [Test]
    public void NoNotesLeaveTheTailAsItWasTest()
    {
        Assert.That(CcxNoteLog.Compose(null, []), Is.Null, "a successful variant without notes keeps a null tail");
        Assert.That(CcxNoteLog.Compose(" *ERROR in e_c3d\n Job failed", []), Is.EqualTo(" *ERROR in e_c3d\n Job failed"));
    }

    [Test]
    public void NotesFollowTheSolverTailAndTheSeparatorTest()
    {
        var tail = CcxNoteLog.Compose(" Job finished\n",
        [
            new CcxNote { Kind = CcxNoteKind.Warning, Text = "SOLVER=PASTIX is not linked in the bundled ccx of this node: the parameter was removed from *STATIC and the solve used PARDISO" },
            new CcxNote { Kind = CcxNoteKind.Info, Column = "rf_sum_z@fix", Text = "9 of 9 rows were printed in the local frame of *TRANSFORM, NSET=FIX and rotated to the global frame" }
        ]);

        Assert.That(tail, Is.EqualTo(
            " Job finished\n" +
            "--- notes ---\n" +
            "[note]\tWARNING\t\tSOLVER=PASTIX is not linked in the bundled ccx of this node: the parameter was removed from *STATIC and the solve used PARDISO\n" +
            "[note]\tINFO\trf_sum_z@fix\t9 of 9 rows were printed in the local frame of *TRANSFORM, NSET=FIX and rotated to the global frame"));
    }

    [Test]
    public void ASuccessfulSolveStartsWithTheSeparatorTest()
    {
        var tail = CcxNoteLog.Compose(null, [new CcxNote { Kind = CcxNoteKind.Error, Text = "response extraction failed: IOException: disk full" }]);

        Assert.That(tail, Is.EqualTo("--- notes ---\n[note]\tERROR\t\tresponse extraction failed: IOException: disk full"));
    }

    [Test]
    public void TabsAndLineBreaksInsideANoteBecomeSpacesTest()
    {
        var line = CcxNoteLog.Line(new CcxNote { Kind = CcxNoteKind.Warning, Column = "max\tvon", Text = "line one\r\nline two" });

        Assert.That(line, Is.EqualTo("[note]\tWARNING\tmax von\tline one  line two"));
        Assert.That(line.Split('\t'), Has.Length.EqualTo(4));
    }

    #endregion

    #region Parse Tests

    [Test]
    public void ATailRoundTripsThroughParseTest()
    {
        CcxNote[] notes =
        [
            new CcxNote { Kind = CcxNoteKind.Warning, Column = "max_von_mises[step 2]", Text = "not reported: STRESS at node 2 reads 'nan' in the .frd, which is not a number" },
            new CcxNote { Kind = CcxNoteKind.Info, Text = "step 2: the reaction forces ccx printed per mode after the eigenvalue solve are mode shapes and are not summed" },
            new CcxNote { Kind = CcxNoteKind.Error, Text = "response extraction failed: OutOfMemoryException: ..." }
        ];

        var (solverTail, parsed) = CcxNoteLog.Parse(CcxNoteLog.Compose(" *ERROR\n exit 201", notes));

        Assert.That(solverTail, Is.EqualTo(" *ERROR\n exit 201"));
        Assert.That(parsed.Select(note => note.Is(notes[parsed.IndexOf(note)])), Is.All.True);
        Assert.That(parsed, Has.Count.EqualTo(3));
    }

    [Test]
    public void ATailWithoutTheSeparatorCarriesNoNotesTest()
    {
        var (solverTail, notes) = CcxNoteLog.Parse(" *ERROR in linstatic: the PASTIX library is not linked\n");

        Assert.That(solverTail, Is.EqualTo(" *ERROR in linstatic: the PASTIX library is not linked\n"));
        Assert.That(notes, Is.Empty);
        Assert.That(CcxNoteLog.Parse(null).Notes, Is.Empty);
        Assert.That(CcxNoteLog.Parse(null).SolverTail, Is.Null);
    }

    [Test]
    public void LinesAfterTheSeparatorThatAreNotNotesAreSkippedTest()
    {
        var (solverTail, notes) = CcxNoteLog.Parse("--- notes ---\nstray line\n[note]\tINFO\tmax_temp\tsomething\n[note]\tBROKEN");

        Assert.That(solverTail, Is.Null);
        Assert.That(notes.Single().Column, Is.EqualTo("max_temp"));
        Assert.That(notes.Single().Kind, Is.EqualTo(CcxNoteKind.Info));
    }

    #endregion
}
