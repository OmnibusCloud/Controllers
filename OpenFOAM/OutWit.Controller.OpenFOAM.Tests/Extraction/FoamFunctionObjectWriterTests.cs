using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Recipes;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Extraction;

[TestFixture]
public class FoamFunctionObjectWriterTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-fo");
        Directory.CreateDirectory(Path.Combine(m_case, "system"));
        File.WriteAllText(Path.Combine(m_case, "system", "controlDict"), "application simpleFoam;\n");
    }

    [TearDown]
    public void TearDown()
    {
        OpenFOAMTestPaths.TryDelete(m_case);
    }

    #region Tools

    private static FoamExtractionRequestData MotorBikeRequest()
    {
        return new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = "coeffs",
                    Kind = FoamResponseKind.ForceCoeffs,
                    Patches = ["\"motorBike.*\""],
                    Parameters =
                    [
                        new FoamNamedValueData { Name = "rho", Value = "rhoInf" },
                        new FoamNamedValueData { Name = "rhoInf", Value = "1" },
                        new FoamNamedValueData { Name = "CofR", Value = "(0.72 0 0)" },
                        new FoamNamedValueData { Name = "liftDir", Value = "(0 0 1)" },
                        new FoamNamedValueData { Name = "dragDir", Value = "(1 0 0)" },
                        new FoamNamedValueData { Name = "pitchAxis", Value = "(0 1 0)" },
                        new FoamNamedValueData { Name = "magUInf", Value = "20" },
                        new FoamNamedValueData { Name = "lRef", Value = "1.42" },
                        new FoamNamedValueData { Name = "Aref", Value = "0.75" }
                    ]
                },
                new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Fields = ["p"], Operation = "areaAverage" },
                new FoamResponseSpecData { Name = "pRange", Kind = FoamResponseKind.FieldMinMax, Fields = ["p", "U"] },
                new FoamResponseSpecData
                {
                    Name = "wake",
                    Kind = FoamResponseKind.Probe,
                    Fields = ["U"],
                    Parameters = [new FoamNamedValueData { Name = "probeLocations", Value = "((2 0 0.5) (3 0 0.5))" }]
                }
            ]
        };
    }

    #endregion

    #region Writer Tests

    [Test]
    public void EveryResponseBecomesADictionaryUnderSystemTest()
    {
        var findings = FoamFunctionObjectWriter.Write(m_case, MotorBikeRequest());

        Assert.That(findings, Is.Empty);
        var coeffs = File.ReadAllText(Path.Combine(m_case, "system", "coeffs"));
        Assert.That(coeffs, Does.Contain("type            forceCoeffs;").And.Contain("libs            (forces);"));
        Assert.That(coeffs, Does.Contain("patches         (\"motorBike.*\");"));
        Assert.That(coeffs, Does.Contain("magUInf         20;").And.Contain("CofR            (0.72 0 0);"));

        var inlet = File.ReadAllText(Path.Combine(m_case, "system", "inletP"));
        Assert.That(inlet, Does.Contain("type            surfaceFieldValue;").And.Contain("name            inlet;").And.Contain("operation       areaAverage;").And.Contain("fields          (p);"));

        Assert.That(File.ReadAllText(Path.Combine(m_case, "system", "pRange")), Does.Contain("type            fieldMinMax;").And.Contain("fields          (p U);"));
        Assert.That(File.ReadAllText(Path.Combine(m_case, "system", "wake")), Does.Contain("type            probes;").And.Contain("probeLocations  ((2 0 0.5) (3 0 0.5));"));
    }

    [Test]
    public void TheWrittenDictionariesPassTheCaseInspectorTest()
    {
        FoamFunctionObjectWriter.Write(m_case, MotorBikeRequest());

        Assert.That(FoamCaseInspector.Inspect(m_case, _ => true), Is.Empty);
    }

    [Test]
    public void AMalformedRequestIsRefusedAndNothingIsWrittenTest()
    {
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "bad name", Kind = FoamResponseKind.Forces, Patches = ["wall"] },
                new FoamResponseSpecData { Name = "noPatch", Kind = FoamResponseKind.ForceCoeffs },
                new FoamResponseSpecData { Name = "twoPatches", Kind = FoamResponseKind.PatchValue, Patches = ["a", "b"], Fields = ["p"], Operation = "areaAverage" },
                new FoamResponseSpecData { Name = "noLocations", Kind = FoamResponseKind.Probe, Fields = ["U"] },
                new FoamResponseSpecData { Name = "code", Kind = FoamResponseKind.FieldMinMax, Fields = ["p"], Parameters = [new FoamNamedValueData { Name = "x", Value = "#calc \"1\"" }] },
                new FoamResponseSpecData { Name = "noPatch", Kind = FoamResponseKind.Forces, Patches = ["wall"] }
            ]
        };

        var findings = FoamFunctionObjectWriter.Write(m_case, request);

        Assert.That(findings, Has.Count.EqualTo(6));
        Assert.That(findings, Has.Some.Contains("'bad name' is not a word"));
        Assert.That(findings, Has.Some.Contains("needs at least one patch"));
        Assert.That(findings, Has.Some.Contains("exactly one patch"));
        Assert.That(findings, Has.Some.Contains("probeLocations"));
        Assert.That(findings, Has.Some.Contains("not a plain dictionary value"));
        Assert.That(findings, Has.Some.Contains("requested twice"));
        Assert.That(Directory.EnumerateFiles(Path.Combine(m_case, "system")).Select(Path.GetFileName), Is.EqualTo(new[] { "controlDict" }));
    }

    [Test]
    public void AResponseNamedLikeAFileTheCaseShipsIsRefusedAndNothingIsWrittenTest()
    {
        File.WriteAllText(Path.Combine(m_case, "system", "coeffs"), "the user's own dictionary\n");

        var findings = FoamFunctionObjectWriter.Write(m_case, MotorBikeRequest());

        Assert.That(findings, Is.EqualTo(new[] { "Response 'coeffs': the case already carries system/coeffs; choose another response name." }));
        Assert.That(File.ReadAllText(Path.Combine(m_case, "system", "coeffs")), Is.EqualTo("the user's own dictionary\n"), "the user's file is untouched");
        Assert.That(File.Exists(Path.Combine(m_case, "system", "inletP")), Is.False, "the other responses are not written either");
    }

    [Test]
    public void ANullOrEmptyRequestWritesNothingTest()
    {
        Assert.That(FoamFunctionObjectWriter.Write(m_case, null), Is.Empty);
        Assert.That(FoamFunctionObjectWriter.Write(m_case, new FoamExtractionRequestData()), Is.Empty);
        Assert.That(Directory.EnumerateFiles(Path.Combine(m_case, "system")).Count(), Is.EqualTo(1));
    }

    #endregion
}
