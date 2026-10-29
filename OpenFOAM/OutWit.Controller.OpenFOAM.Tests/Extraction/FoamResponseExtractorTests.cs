using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

[TestFixture]
public class FoamResponseExtractorTests
{
    private const string COEFFICIENT_DAT =
        "# Force coefficients\n# dragDir   : (1 0 0)\n# liftDir   : (0 0 1)\n" +
        "# Time        \tCd            \tCd(f)         \tCd(r)         \tCl            \tCl(f)         \tCl(r)         \tCmPitch\n" +
        "100\t0.44\t0.30\t0.14\t0.10\t0.06\t0.04\t0.01\n" +
        "200\t0.418\t0.281\t0.137\t0.092\t0.055\t0.037\t0.009\n";

    /// <summary>A probes file as v2606 writes it: the probes, then "# Time" with their indices.</summary>
    private const string PROBES_P =
        "# Probe 0 (0.1 0.2 0)\n# Probe 1 (0.3 0.2 0)\n# Time        0             1            \n" +
        "        0.1     -1.23e+00     -0.5e+00\n        0.2     -1.20e+00     -0.48e+00\n";

    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-post");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private void Write(string relative, string text)
    {
        var path = Path.Combine(m_case, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    #endregion

    #region Extraction Tests

    [Test]
    public void ValuesAreNamedByResponseAndColumnAndTheTimeColumnIsSkippedTest()
    {
        Write("postProcessing/coeffs/0/coefficient.dat", "# Time Cd\n0 9\n");
        Write("postProcessing/coeffs/200/coefficient.dat", COEFFICIENT_DAT);
        Write("postProcessing/probes/0/p", PROBES_P);
        Write("postProcessing/probes/0/U", "# Probe 0 (0.1 0.2 0)\n# Time  0\n0.2  (1 2 3)\n");

        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs },
                new FoamResponseSpecData { Name = "probes", Kind = FoamResponseKind.Probe, Fields = ["p", "U"] },
                new FoamResponseSpecData { Name = "absent", Kind = FoamResponseKind.FieldMinMax }
            ]
        };

        var row = FoamResponseExtractor.Extract(m_case, request);

        var names = row.Values.Select(value => value.Name).ToList();
        Assert.That(names, Does.Contain("coeffs.Cd").And.Contain("coeffs.CmPitch"));
        Assert.That(names, Does.Not.Contain("coeffs.Time"));
        Assert.That(row.Values.First(value => value.Name == "coeffs.Cd").Value, Is.EqualTo(0.418), "the LATEST time directory is read");
        Assert.That(names, Does.Contain("probes.p@probe1(0.3 0.2 0)").And.Contain("probes.U@probe0(0.1 0.2 0)_z"),
            "a probe column carries its field, number, position and component - the file's name is not repeated");
        Assert.That(row.Values.First(value => value.Name == "probes.U@probe0(0.1 0.2 0)_z").Value, Is.EqualTo(3));
        Assert.That(names.Count(name => name.StartsWith("absent", StringComparison.Ordinal)), Is.EqualTo(0), "a response whose function object wrote nothing contributes nothing");
    }

    [Test]
    public void ARowBeforeTheFinalTimeIsNotReportedTest()
    {
        // A function object that stopped writing before the run ended: its
        // last row is not the final state, and a stale value is worse than none.
        Write("postProcessing/coeffs/0/coefficient.dat", COEFFICIENT_DAT);
        var request = new FoamExtractionRequestData { Responses = [new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs }] };
        var notes = new List<FoamNote>();

        var row = FoamResponseExtractor.Extract(m_case, request, 250, notes);

        Assert.That(row.Values, Is.Empty);
        Assert.That(FoamResponseExtractor.Sentences(notes), Is.EqualTo(new[] { "coeffs: the last row of coefficient.dat is at time 200, the run ended at 250 - not reported." }));
        Assert.That(notes.Select(note => note.Column), Is.EqualTo(new[]
        {
            "coeffs.Cd", "coeffs.Cd(f)", "coeffs.Cd(r)", "coeffs.Cl", "coeffs.Cl(f)", "coeffs.Cl(r)", "coeffs.CmPitch"
        }), "the sentence on every column the file would have reported, named as the row names it; the time column is none");
        Assert.That(notes.Select(note => note.Kind), Is.All.EqualTo(FoamNoteKind.Warning));
    }

    [Test]
    public void AStaleFilesNoteIsOnEveryValueColumnButAWordTest()
    {
        // A case's own fieldMinMax writes the field's name in a column of
        // its own (v2606, location on): a word, never a value. A probe
        // outside the mesh in a stale file is a value column like any.
        Write("postProcessing/ownMinMax/0/fieldMinMax.dat", "# Time field min max\n0.04 p -2.0 2.3\n");
        Write("postProcessing/wake/0/p", "# Probe 0 (0.1 0 0)\n# Probe 1 (9 9 9)  # Not Found\n# Time 0 1\n0.04 -3.68615 -1e+300\n");
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "ownMinMax", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"] },
                new FoamResponseSpecData { Name = "wake", Kind = FoamResponseKind.Probe, Fields = ["p"] }
            ]
        };
        var notes = new List<FoamNote>();

        var row = FoamResponseExtractor.Extract(m_case, request, 0.045, notes);

        Assert.That(row.Values, Is.Empty);
        Assert.That(notes.Select(note => note.Column), Is.EqualTo(new[] { "ownMinMax.min", "ownMinMax.max", "wake.p@probe0(0.1 0 0)", "wake.p@probe1(9 9 9)" }));
        Assert.That(FoamResponseExtractor.Sentences(notes), Is.EqualTo(new[]
        {
            "ownMinMax: the last row of fieldMinMax.dat is at time 0.04, the run ended at 0.045 - not reported.",
            "wake: the last row of p is at time 0.04, the run ended at 0.045 - not reported."
        }), "stale says it all: the probe outside the mesh is not named a second time");
    }

    [Test]
    public void ARowAtTheFinalTimeIsReportedTest()
    {
        Write("postProcessing/coeffs/0/coefficient.dat", COEFFICIENT_DAT);
        Write("postProcessing/probes/0/p", PROBES_P);
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs },
                new FoamResponseSpecData { Name = "probes", Kind = FoamResponseKind.Probe, Fields = ["p"] }
            ]
        };
        var notes = new List<FoamNote>();

        // Transient times carry the last digit's noise of the time loop.
        Assert.That(FoamResponseExtractor.Extract(m_case, request, 200, notes).Values.Select(value => value.Name), Does.Contain("coeffs.Cd"));
        Assert.That(FoamResponseExtractor.Extract(m_case, request, 0.20000000000000004, notes).Values.Select(value => value.Name), Does.Contain("probes.p@probe0(0.1 0.2 0)"));
        Assert.That(FoamResponseExtractor.Sentences(notes), Has.Count.EqualTo(2), "each call judges its own final time: coeffs at 200 is stale against 0.2, probes at 0.2 against 200");
    }

    [Test]
    public void TheNotesAreWrittenBesideTheStepLogsTest()
    {
        const string SENTENCE = "coeffs: the last row of coefficient.dat is at time 200, the run ended at 250 - not reported.";
        const string FOLLOWS = "Response 'coeffs': lRef follows the case's coeffs (system/controlDict:6), which the study sweeps - 2 in this variant.";
        FoamResponseExtractor.WriteNotes(m_case,
        [
            new FoamNote { Kind = FoamNoteKind.Warning, Column = "coeffs.Cd", Text = SENTENCE },
            new FoamNote { Kind = FoamNoteKind.Warning, Column = "coeffs.Cl", Text = SENTENCE },
            new FoamNote { Kind = FoamNoteKind.Info, Text = FOLLOWS }
        ]);

        Assert.That(File.ReadAllLines(Path.Combine(m_case, "log.responses")), Is.EqualTo(new[] { SENTENCE, FOLLOWS }),
            "one line per sentence, as before: a sentence on several columns is written once");

        File.Delete(Path.Combine(m_case, "log.responses"));
        FoamResponseExtractor.WriteNotes(m_case, []);
        Assert.That(File.Exists(Path.Combine(m_case, "log.responses")), Is.False, "no notes, no file");
    }

    [Test]
    public void AMultiFieldMinMaxRowIsReadWholeTest()
    {
        // v2606 writes every field of a fieldMinMax in one row of one file.
        Write("postProcessing/pRange/0/fieldMinMax.dat", "# Time min(p) max(p) min(U) max(U)\n281 -8.4 12.1 0 10.2\n");

        var row = FoamResponseExtractor.Extract(m_case, new FoamExtractionRequestData
        {
            Responses = [new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p", "U"] }]
        });

        Assert.That(row.Values.Select(value => value.Name), Is.EqualTo(new[] { "pRange.min(p)", "pRange.max(p)", "pRange.min(U)", "pRange.max(U)" }));
        Assert.That(row.Values[1].Value, Is.EqualTo(12.1));
    }

    [Test]
    public void AProbeOutsideTheMeshIsAnEmptyCellWithANoteTest()
    {
        // v2606 writes -1e+300 (its -VGREAT) for a probe it did not find and
        // marks the probe in the header: before, that number went into the
        // response table as a measurement.
        Write("postProcessing/wake/0/p", "# Probe 0 (0.1 0 0)\n# Probe 1 (9 9 9)  # Not Found\n# Time 0 1\n282 -3.68615 -1e+300\n");
        Write("postProcessing/wake/0/U", "# Probe 0 (0.1 0 0)\n# Probe 1 (9 9 9)  # Not Found\n# Time 0 1\n282 (6.4 -0.5 0) (-1e+300 -1e+300 -1e+300)\n");
        var request = new FoamExtractionRequestData { Responses = [new FoamResponseSpecData { Name = "wake", Kind = FoamResponseKind.Probe, Fields = ["p", "U"] }] };
        var notes = new List<FoamNote>();

        var row = FoamResponseExtractor.Extract(m_case, request, 282, notes);

        Assert.That(row.Values.Select(value => value.Name), Is.EqualTo(new[]
        {
            "wake.U@probe0(0.1 0 0)_x", "wake.U@probe0(0.1 0 0)_y", "wake.U@probe0(0.1 0 0)_z", "wake.p@probe0(0.1 0 0)"
        }), "the probe outside the mesh is an empty cell: no value under its name");
        Assert.That(notes.Select(note => (note.Kind, note.Column, note.Text)), Is.EqualTo(new[]
        {
            (FoamNoteKind.Warning, (string?)"wake.U@probe1(9 9 9)_x", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            (FoamNoteKind.Warning, (string?)"wake.U@probe1(9 9 9)_y", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            (FoamNoteKind.Warning, (string?)"wake.U@probe1(9 9 9)_z", "wake: probe 1 (9 9 9) of U is outside the mesh - not reported."),
            (FoamNoteKind.Warning, (string?)"wake.p@probe1(9 9 9)", "wake: probe 1 (9 9 9) of p is outside the mesh - not reported.")
        }), "the empty cells, each named as the row would have named its value");
        Assert.That(FoamResponseExtractor.Sentences(notes), Is.EqualTo(new[]
        {
            "wake: probe 1 (9 9 9) of U is outside the mesh - not reported.",
            "wake: probe 1 (9 9 9) of p is outside the mesh - not reported."
        }), "log.responses: one sentence per probe and field, as before");
    }

    [Test]
    public void ANullRequestYieldsAnEmptyRowTest()
    {
        Assert.That(FoamResponseExtractor.Extract(m_case, null).Values, Is.Empty);
        Assert.That(FoamResponseExtractor.Extract(m_case, new FoamExtractionRequestData()).Values, Is.Empty);
    }

    [Test]
    public void ACaseWithoutPostProcessingYieldsAnEmptyRowTest()
    {
        var row = FoamResponseExtractor.Extract(m_case, new FoamExtractionRequestData
        {
            Responses = [new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs }]
        });

        Assert.That(row.Values, Is.Empty);
    }

    #endregion

    #region Time Directory Tests

    [Test]
    public void TheLatestRowOfAFileWinsOverTheLatestDirectoryTest()
    {
        // A response measured during the solve writes under the start time,
        // the recipe's post step under the latest written time (0.04 when
        // the run ended at 0.045): the row at the final time is the solve's,
        // in the earlier directory. On a tie the solve's row wins too.
        Write("postProcessing/inletP/0/surfaceFieldValue.dat", "# Time areaAverage(p)\n0.04 -5.4\n0.045 -5.5\n");
        Write("postProcessing/inletP/0.04/surfaceFieldValue.dat", "# Time areaAverage(p)\n0.04 -5.4\n");
        Write("postProcessing/tie/0/volFieldValue.dat", "# Time volAverage(k)\n0.04 1\n");
        Write("postProcessing/tie/0.04/volFieldValue.dat", "# Time volAverage(k)\n0.04 2\n");
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue },
                new FoamResponseSpecData { Name = "tie", Kind = FoamResponseKind.VolumeValue }
            ]
        };
        var notes = new List<FoamNote>();

        var row = FoamResponseExtractor.Extract(m_case, request, 0.045, notes);

        Assert.That(row.Values.First(value => value.Name == "inletP.areaAverage(p)").Value, Is.EqualTo(-5.5), "the solve's row at the final time, not the post step's stale one");
        Assert.That(notes.Select(note => (note.Column, note.Text)), Is.EqualTo(new[]
        {
            ((string?)"tie.volAverage(k)", "tie: the last row of volFieldValue.dat is at time 0.04, the run ended at 0.045 - not reported.")
        }), "inletP is not stale; tie is, whichever row wins");
        Assert.That(FoamResponseExtractor.Extract(m_case, request).Values.First(value => value.Name == "tie.volAverage(k)").Value, Is.EqualTo(1), "on a tie, the earlier directory");
        Assert.That(FoamResponseExtractor.LatestRows(Path.Combine(m_case, "postProcessing", "inletP")).Keys, Is.EqualTo(new[] { "surfaceFieldValue.dat" }));
        Assert.That(FoamResponseExtractor.LatestRows(Path.Combine(m_case, "postProcessing", "none")), Is.Empty);
    }

    [Test]
    public void AWritersRestartSiblingIsTheSameMeasurementTest()
    {
        // v2606 writes surfaceFieldValue_0.dat beside surfaceFieldValue.dat
        // when a post step runs after the solve in the same start-time
        // directory: one measurement, one column name, the later row.
        Write("postProcessing/inletP/0/surfaceFieldValue.dat", "# Time areaAverage(p)\n0.045 -5.5\n");
        Write("postProcessing/inletP/0/surfaceFieldValue_0.dat", "# Time areaAverage(p)\n0.04 -5.4\n");
        Write("postProcessing/wake/0/T", "# Probe 0 (1 1 1)\n# Time 0\n0.045 300\n");
        Write("postProcessing/wake/0/T_1", "# Probe 0 (1 1 1)\n# Time 0\n0.045 301\n");
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue },
                new FoamResponseSpecData { Name = "wake", Kind = FoamResponseKind.Probe, Fields = ["T", "T_1"] }
            ]
        };

        var row = FoamResponseExtractor.Extract(m_case, request);

        Assert.That(row.Values.Select(value => (value.Name, value.Value)), Is.EqualTo(new[]
        {
            ("inletP.areaAverage(p)", -5.5), ("wake.T@probe0(1 1 1)", 300.0), ("wake.T_1@probe0(1 1 1)", 301.0)
        }), "the .dat sibling folds into its stem; probe files, which are fields, never do");
    }

    [Test]
    public void TheLatestTimeDirectoryIsChosenNumericallyTest()
    {
        foreach (var time in new[] { "0", "100", "1e-05", "250.5", "notATime" })
            Directory.CreateDirectory(Path.Combine(m_case, "postProcessing", "coeffs", time));

        var latest = FoamResponseExtractor.LatestTimeDirectory(Path.Combine(m_case, "postProcessing", "coeffs"));

        Assert.That(Path.GetFileName(latest), Is.EqualTo("250.5"));
        Assert.That(FoamResponseExtractor.LatestTimeDirectory(Path.Combine(m_case, "postProcessing", "nothing")), Is.Null);
    }

    #endregion
}
