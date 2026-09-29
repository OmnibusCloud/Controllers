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
                new FoamResponseSpecData { Name = "coeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["\"(motorBike|wheel).*\""] },
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
    public void AResponseNamedLikeAFileOfTheCasesSystemIsRefusedTest()
    {
        var request = new FoamExtractionRequestData
        {
            Responses = [new FoamResponseSpecData { Name = "forceCoeffs", Kind = FoamResponseKind.ForceCoeffs, Patches = ["wall"] }]
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
                    Parameters = [new FoamNamedValueData { Name = "magUInf", Value = value }]
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
            Responses = [new FoamResponseSpecData { Name = name, Kind = FoamResponseKind.Forces, Patches = [patch] }]
        };

        Assert.That(FoamResponseRules.Validate(Load("load.x", "wall")), Is.EqualTo(new[] { "Response name 'load.x' is not a word (letters, digits, underscore)." }),
            "a response name is a column prefix and a file of system/");
        Assert.That(FoamResponseRules.Validate(Load("load", "wall.a")), Is.EqualTo(new[] { "Response 'load': 'wall.a' is not a patch name." }),
            "the dot is a field's, not a patch's");
    }

    #endregion
}
