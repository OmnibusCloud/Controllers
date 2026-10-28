using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamResponseRulesTests
{
    #region Validation Tests

    [Test]
    public void AWellFormedRequestHasNoFindingsTest()
    {
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["\"(motorBike|wheel).*\""], Parameters = [new FoamNamedValueData { Name = "CofR", Value = "(0 0 0)" }, new FoamNamedValueData { Name = "liftDir", Value = "(0 0 1)" }, new FoamNamedValueData { Name = "dragDir", Value = "(1 0 0)" }, new FoamNamedValueData { Name = "magUInf", Value = "20" }, new FoamNamedValueData { Name = "lRef", Value = "1" }, new FoamNamedValueData { Name = "Aref", Value = "1" }, new FoamNamedValueData { Name = "rhoInf", Value = "1" }] },
                new FoamResponseSpecData { Name = "inletP", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Fields = ["p"], Operation = "areaAverage" },
                new FoamResponseSpecData { Name = "cellT", Kind = FoamResponseKind.VolumeValue, Fields = ["T"], Operation = "volAverage" },
                new FoamResponseSpecData { Name = "wake", Kind = FoamResponseKind.Probe, Fields = ["U"], Parameters = [new FoamNamedValueData { Name = FoamResponseRules.PROBE_LOCATIONS, Value = "((2 0 0.5))" }] }
            ]
        };

        Assert.That(FoamResponseRules.Validate(request), Is.Empty);
        Assert.That(FoamResponseRules.Validate(null), Is.Empty);
    }

    [Test]
    public void EachKindNeedsItsPartsTest()
    {
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData { Name = "forces", Kind = FoamResponseKind.Forces },
                new FoamResponseSpecData { Name = "patch", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Operation = "area Average" },
                new FoamResponseSpecData { Name = "volume", Kind = FoamResponseKind.VolumeValue, Operation = "volAverage" },
                new FoamResponseSpecData { Name = "range", Kind = FoamResponseKind.FieldMinMax },
                new FoamResponseSpecData { Name = "probe", Kind = FoamResponseKind.Probe, Fields = ["U"] }
            ]
        };

        var findings = FoamResponseRules.Validate(request);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "Response 'forces': Forces needs at least one patch.",
            "Response 'forces': Forces needs CofR - OpenFOAM does not start the function object without them.",
            "Response 'patch': a patch value needs at least one field.",
            "Response 'patch': 'area Average' is not an operation (areaAverage, areaIntegrate, min, max, ...).",
            "Response 'volume': a volume value needs at least one field.",
            "Response 'range': a min/max needs at least one field.",
            "Response 'probe': a probe needs a 'probeLocations' parameter."
        }));
    }

    [Test]
    public void CodeNeverPassesAsANameAPatchOrAValueTest()
    {
        var request = new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = "range",
                    Kind = FoamResponseKind.FieldMinMax,
                    Patches = ["wall;#calc", "\"a b\""],
                    Fields = ["p{"],
                    Parameters = [new FoamNamedValueData { Name = "x", Value = "#codeStream{}" }, new FoamNamedValueData { Name = "1bad", Value = "1" }]
                }
            ]
        };

        var findings = FoamResponseRules.Validate(request);

        Assert.That(findings, Has.Count.EqualTo(5));
        Assert.That(findings, Has.Some.Contains("'wall;#calc' is not a patch name"));
        Assert.That(findings, Has.Some.Contains("'\"a b\"' is not a patch name"));
        Assert.That(findings, Has.Some.Contains("'p{' is not a field name"));
        Assert.That(findings, Has.Some.Contains("the value of 'x' is not a plain dictionary value"));
        Assert.That(findings, Has.Some.Contains("parameter '1bad' is not a keyword"));
    }

    [Test]
    public void AForceWithoutItsReferencesIsRefusedTest()
    {
        // OpenFOAM stops the solve on a force function object without them
        // (v2606: "Entry 'magUInf' not found", "Entry 'origin' not found").
        static FoamExtractionRequestData Request(FoamResponseKind kind, params (string Name, string Value)[] parameters) => new()
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = "load", Kind = kind, Patches = ["wall"],
                    Parameters = parameters.Select(parameter => new FoamNamedValueData { Name = parameter.Name, Value = parameter.Value }).ToList()
                }
            ]
        };

        Assert.That(FoamResponseRules.Validate(Request(FoamResponseKind.Forces)),
            Is.EqualTo(new[] { "Response 'load': Forces needs CofR - OpenFOAM does not start the function object without them." }));
        Assert.That(FoamResponseRules.Validate(Request(FoamResponseKind.Forces, ("origin", "(0 0 0)"))), Is.Empty,
            "the coordinate-system spelling of the centre");
        Assert.That(FoamResponseRules.Validate(Request(FoamResponseKind.ForceCoeffs, ("CofR", "(0 0 0)"), ("magUInf", " "))),
            Is.EqualTo(new[] { "Response 'load': ForceCoeffs needs magUInf, lRef, Aref, rhoInf, liftDir, dragDir - OpenFOAM does not start the function object without them." }),
            "a blank value is no value; coefficients read rhoInf whatever rho says, and the directions go with CofR");
    }

    [Test]
    public void TheDensityAndTheDirectionsAForceReadsAreRequiredTest()
    {
        // v2606: forceCoeffs::read reads rhoInf with readEntry when rho names
        // a field, forces::read when rho is rhoInf - so coefficients need it
        // always, a force only under rho rhoInf; and with CofR, liftDir and
        // dragDir are read with get, no default. A coordinateSystem carries the
        // centre and the directions itself.
        static FoamResponseSpecData Spec(FoamResponseKind kind, params (string Name, string Value)[] parameters) => new()
        {
            Name = "load", Kind = kind, Patches = ["wall"],
            Parameters = parameters.Select(parameter => new FoamNamedValueData { Name = parameter.Name, Value = parameter.Value }).ToList()
        };

        Assert.That(FoamResponseRules.MissingReferences(Spec(FoamResponseKind.Forces, ("CofR", "(0 0 0)"), ("rho", "rhoInf"))),
            Is.EqualTo(new[] { "rhoInf" }), "a kinematic force needs the freestream density");
        Assert.That(FoamResponseRules.MissingReferences(Spec(FoamResponseKind.Forces, ("CofR", "(0 0 0)"), ("rho", "rhoInf"), ("rhoInf", "1.2"))), Is.Empty);
        Assert.That(FoamResponseRules.MissingReferences(Spec(FoamResponseKind.Forces, ("CofR", "(0 0 0)"), ("rho", "rho"))), Is.Empty, "a density field needs no rhoInf");

        Assert.That(FoamResponseRules.MissingReferences(Spec(FoamResponseKind.ForceCoeffs,
                ("CofR", "(0 0 0)"), ("rho", "rho"), ("magUInf", "20"), ("lRef", "1"), ("Aref", "1"))),
            Is.EqualTo(new[] { "rhoInf", "liftDir", "dragDir" }), "coefficients over a density field still read rhoInf; CofR brings the directions");
        Assert.That(FoamResponseRules.MissingReferences(Spec(FoamResponseKind.ForceCoeffs,
                ("CofR", "(0 0 0)"), ("liftDir", "(0 0 1)"), ("dragDir", "(1 0 0)"), ("rho", "rhoInf"), ("rhoInf", "1"), ("magUInf", "20"), ("lRef", "1"), ("Aref", "1"))),
            Is.Empty);
        Assert.That(FoamResponseRules.MissingReferences(Spec(FoamResponseKind.ForceCoeffs,
                ("coordinateSystem", "{ origin (0 0 0); e1 (1 0 0); e3 (0 0 1); }"), ("rhoInf", "1"), ("magUInf", "20"), ("lRef", "1"), ("Aref", "1"))),
            Is.Empty, "a coordinate system of the function object's own carries the centre and the directions");

        var findings = FoamResponseRules.Validate(new FoamExtractionRequestData
        {
            Responses = [Spec(FoamResponseKind.ForceCoeffs, ("coordinateSystem", "{ origin (0 0 0); e1 (1 0 0); e3 (0 0 1); }"), ("rhoInf", "1"), ("magUInf", "20"), ("lRef", "1"), ("Aref", "1"))]
        });
        Assert.That(findings, Is.Empty, "a flat sub-dictionary is a plain value");
        Assert.That(FoamResponseRules.IsPlainValue("{ nBin 20; direction (1 0 0); cumulative yes; }"), Is.True);
        Assert.That(FoamResponseRules.IsPlainValue("{ inner { x 1; } }"), Is.False, "no nesting");
        Assert.That(FoamResponseRules.IsPlainValue("{ x #calc \"1\"; }"), Is.False, "no code");
    }

    [Test]
    public void AResponseNamedLikeAFileOfTheCasesSystemIsRefusedTest()
    {
        var request = new FoamExtractionRequestData
        {
            Responses = [new FoamResponseSpecData { Name = "forceCoeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["wall"], Parameters = [new FoamNamedValueData { Name = "origin", Value = "(0 0 0)" }, new FoamNamedValueData { Name = "magUInf", Value = "20" }, new FoamNamedValueData { Name = "lRef", Value = "1" }, new FoamNamedValueData { Name = "Aref", Value = "1" }, new FoamNamedValueData { Name = "rhoInf", Value = "1" }] }]
        };

        Assert.That(FoamResponseRules.Validate(request, ["system/controlDict", "system/forceCoeffs"]),
            Is.EqualTo(new[] { "Response 'forceCoeffs': the case already carries system/forceCoeffs; choose another response name." }));
        Assert.That(FoamResponseRules.Validate(request, ["constant/forceCoeffs"]), Is.Empty, "only system/ is where a response is written");
    }

    [Test]
    public void AQuotedSelectorIsPlainRegularExpressionTextTest()
    {
        Assert.That(FoamResponseRules.IsQuotedRegex("\"(motorBike|wall).*\""), Is.True);
        Assert.That(FoamResponseRules.IsQuotedRegex("\"wall\""), Is.True);
        Assert.That(FoamResponseRules.IsQuotedRegex("wall"), Is.False, "not quoted");
        Assert.That(FoamResponseRules.IsQuotedRegex("\"\""), Is.False, "empty");
        Assert.That(FoamResponseRules.IsQuotedRegex("\"a;b\""), Is.False, "a semicolon ends a dictionary entry");
    }

    [Test]
    public void APlaceholderStandsForAValueTheSweepSuppliesTest()
    {
        static FoamExtractionRequestData Coefficients(string value) => new()
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["body"],
                    Parameters =
                    [
                        new FoamNamedValueData { Name = "magUInf", Value = value },
                        new FoamNamedValueData { Name = "CofR", Value = "(0 0 0)" },
                        new FoamNamedValueData { Name = "liftDir", Value = "(0 0 1)" },
                        new FoamNamedValueData { Name = "dragDir", Value = "(1 0 0)" },
                        new FoamNamedValueData { Name = "lRef", Value = "1" },
                        new FoamNamedValueData { Name = "Aref", Value = "1" },
                        new FoamNamedValueData { Name = "rhoInf", Value = "1" }
                    ]
                }
            ]
        };

        Assert.That(FoamResponseRules.Validate(Coefficients("{{oc1}}")), Is.Empty, "a speed swept with the case");
        Assert.That(FoamResponseRules.Validate(Coefficients("({{oc1}} 0 0)")), Is.Empty, "a placeholder inside a vector");
        Assert.That(FoamResponseRules.Validate(Coefficients("{{oc1}};#calc")), Has.Some.Contains("the value of 'magUInf' is not a plain dictionary value"));
        Assert.That(FoamResponseRules.Validate(Coefficients("{{name}}")), Has.Some.Contains("not a plain dictionary value"), "only the sweep's own token shape");
    }

    [Test]
    public void APhaseFieldNamedWithADotIsAFieldTest()
    {
        static FoamExtractionRequestData Depth(string field) => new()
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = "depth", Kind = FoamResponseKind.PatchValue, Patches = ["inlet"], Fields = [field], Operation = "areaIntegrate"
                }
            ]
        };

        Assert.That(FoamResponseRules.Validate(Depth("alpha.water")), Is.Empty, "every multiphase solver names its phases so");
        Assert.That(FoamResponseRules.Validate(Depth("T.solid")), Is.Empty, "and a region's fields");
        Assert.That(FoamResponseRules.Validate(Depth(".water")), Has.Some.Contains("'.water' is not a field name"), "a word starts with a letter");
        Assert.That(FoamResponseRules.Validate(Depth("U:Transformed")), Has.Some.Contains("'U:Transformed' is not a field name"),
            "a colon is no file name on a Windows node, and a probe writes one file per field");
    }

    [Test]
    public void APatchOrAResponseNameStaysAWordWithoutADotTest()
    {
        static FoamExtractionRequestData Load(string name, string patch) => new()
        {
            Responses = [new FoamResponseSpecData { Name = name, Kind = FoamResponseKind.Forces, Patches = [patch], Parameters = [new FoamNamedValueData { Name = "CofR", Value = "(0 0 0)" }] }]
        };

        Assert.That(FoamResponseRules.Validate(Load("load.x", "wall")), Is.EqualTo(new[] { "Response name 'load.x' is not a word (letters, digits, underscore)." }),
            "a response name is a column prefix and a file of system/");
        Assert.That(FoamResponseRules.Validate(Load("load", "wall.a")), Is.EqualTo(new[] { "Response 'load': 'wall.a' is not a patch name." }),
            "the dot is a field's, not a patch's");
    }

    #endregion
}
