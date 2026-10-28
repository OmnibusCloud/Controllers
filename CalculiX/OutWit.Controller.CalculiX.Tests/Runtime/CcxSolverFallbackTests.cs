using OutWit.Controller.CalculiX.Extraction;
using OutWit.Controller.CalculiX.Runtime;

namespace OutWit.Controller.CalculiX.Tests.Runtime;

/// <summary>
/// A step card naming a solver the bundled ccx does not link ends the solve
/// with exit 201 (PASTIX, TAUCS, SGI everywhere; PARDISO on macOS): the
/// parameter is removed before the solve and the variant carries a note.
/// </summary>
[TestFixture]
public class CcxSolverFallbackTests
{
    private string m_directory = null!;

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"ccx-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    #region Rewrite Tests

    [Test]
    public void AnUnlinkedSolverIsRemovedFromItsCardInAnySpellingTest()
    {
        var rewritten = CcxSolverFallback.Rewrite(
            ["*STEP", "*STATIC, SOLVER=PASTIX", "1., 1.", "*END STEP", "*STEP", "*Frequency, Solver = PaStiX, STORAGE=YES", "5", "*END STEP"],
            pardisoLinked: true,
            out var notes);

        Assert.That(rewritten, Is.EqualTo(new[] { "*STEP", "*STATIC", "1., 1.", "*END STEP", "*STEP", "*Frequency, STORAGE=YES", "5", "*END STEP" }));
        Assert.That(notes.Select(note => note.Kind), Is.All.EqualTo(CcxNoteKind.Warning));
        Assert.That(notes.Select(note => note.Text), Is.EqualTo(new[]
        {
            "SOLVER=PASTIX is not linked in the bundled ccx of this node: the parameter was removed from *STATIC and the solve used PARDISO",
            "SOLVER=PASTIX is not linked in the bundled ccx of this node: the parameter was removed from *FREQUENCY and the solve used PARDISO"
        }));
    }

    [Test]
    public void PardisoStaysWhereItIsLinkedAndGoesWhereItIsNotTest()
    {
        string[] deck = ["*STATIC, SOLVER=PARDISO"];

        var windows = CcxSolverFallback.Rewrite(deck, pardisoLinked: true, out var windowsNotes);
        var mac = CcxSolverFallback.Rewrite(deck, pardisoLinked: false, out var macNotes);

        Assert.That(windows, Is.EqualTo(deck));
        Assert.That(windowsNotes, Is.Empty);
        Assert.That(mac, Is.EqualTo(new[] { "*STATIC" }));
        Assert.That(macNotes.Single().Text, Does.EndWith("the solve used SPOOLES"));
    }

    [Test]
    public void LinkedAndUnknownSolversAreLeftAloneTest()
    {
        // ccx warns about a name it does not know and takes its default by
        // itself; SPOOLES and the iterative solvers are always there.
        string[] deck = ["*STATIC, SOLVER=SPOOLES", "*STATIC, SOLVER=ITERATIVECHOLESKY", "*STATIC, SOLVER=BOGUS"];

        var rewritten = CcxSolverFallback.Rewrite(deck, pardisoLinked: false, out var notes);

        Assert.That(rewritten, Is.EqualTo(deck));
        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void AParameterOnAContinuationLineGoesWithItsCommaTest()
    {
        var rewritten = CcxSolverFallback.Rewrite(
            ["*HEAT TRANSFER, STEADY STATE,", "SOLVER=TAUCS", "1., 1.", "*DYNAMIC, DIRECT,", "SOLVER=SGI, ALPHA=-0.3", "0.1, 1."],
            pardisoLinked: true,
            out var notes);

        Assert.That(rewritten, Is.EqualTo(new[] { "*HEAT TRANSFER, STEADY STATE", "1., 1.", "*DYNAMIC, DIRECT,", "ALPHA=-0.3", "0.1, 1." }));
        Assert.That(notes, Has.Count.EqualTo(2));
    }

    [Test]
    public void DataLinesAndCommentsAreNeverTouchedTest()
    {
        string[] deck = ["*HEADING", "SOLVER=PASTIX benchmark", "** SOLVER=PASTIX was the plan", "*STATIC", "1., 1."];

        var rewritten = CcxSolverFallback.Rewrite(deck, pardisoLinked: true, out var notes);

        Assert.That(rewritten, Is.EqualTo(deck));
        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void ApplyRewritesTheDeckInPlaceOnlyWhenItMustTest()
    {
        var path = Path.Combine(m_directory, "job.inp");
        File.WriteAllText(path, "*STEP\n*STATIC, SOLVER=PASTIX\n1., 1.\n*END STEP\n");
        var untouched = Path.Combine(m_directory, "plain.inp");
        File.WriteAllText(untouched, "*STEP\n*STATIC\n1., 1.\n*END STEP\n");
        var stamp = File.GetLastWriteTimeUtc(untouched);

        var notes = CcxSolverFallback.Apply(path, pardisoLinked: true);
        var none = CcxSolverFallback.Apply(untouched, pardisoLinked: true);

        Assert.That(notes, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllLines(path), Is.EqualTo(new[] { "*STEP", "*STATIC", "1., 1.", "*END STEP" }));
        Assert.That(none, Is.Empty);
        Assert.That(File.GetLastWriteTimeUtc(untouched), Is.EqualTo(stamp), "a deck with nothing to change is not rewritten");
    }

    [Test]
    public void TheLinkedSetsMatchTheKitsTest()
    {
        Assert.That(CcxSolverFallback.Linked(pardisoLinked: true), Is.EquivalentTo(new[] { "SPOOLES", "PARDISO", "ITERATIVESCALING", "ITERATIVECHOLESKY" }));
        Assert.That(CcxSolverFallback.Linked(pardisoLinked: false), Is.EquivalentTo(new[] { "SPOOLES", "ITERATIVESCALING", "ITERATIVECHOLESKY" }));
        Assert.That(CcxSolverFallback.Default(pardisoLinked: true), Is.EqualTo("PARDISO"));
        Assert.That(CcxSolverFallback.Default(pardisoLinked: false), Is.EqualTo("SPOOLES"));
    }

    #endregion
}
