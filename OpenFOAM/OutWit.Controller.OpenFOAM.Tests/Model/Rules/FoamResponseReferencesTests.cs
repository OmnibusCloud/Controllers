using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// A force response that re-measures a case's own force function object
/// follows the case where the study sweeps it: the entry's variant value
/// reaches the response the node writes (K4).
/// </summary>
[TestFixture]
public class FoamResponseReferencesTests
{
    #region Constants

    private const string HEAD = "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 10;\n";

    /// <summary>motorBike's forceCoeffs1, its length and area swept, its speed through a top-level reference the study sweeps too.</summary>
    private const string TEMPLATED =
        HEAD + "speed {{oc1}};\nfunctions\n{\n    forceCoeffs1\n    {\n        type forceCoeffs; libs (forces); patches (body);\n" +
        "        rho rhoInf; rhoInf 1; CofR (0.72 0 0); liftDir (0 0 1); dragDir (1 0 0); pitchAxis (0 1 0);\n" +
        "        magUInf $speed; lRef {{oc2}}; Aref 0.75;\n    }\n}\n";

    #endregion

    #region Tools

    private static string Instantiate(string text, string speed, string length)
    {
        return text.Replace("{{oc1}}", speed, StringComparison.Ordinal).Replace("{{oc2}}", length, StringComparison.Ordinal);
    }

    private static FoamExtractionRequestData Request(string name)
    {
        return new FoamExtractionRequestData
        {
            Responses =
            [
                new FoamResponseSpecData
                {
                    Name = name, Kind = FoamResponseKind.ForceCoeffs, Patches = ["body"],
                    Parameters =
                    [
                        new FoamNamedValueData { Name = "rho", Value = "rhoInf" },
                        new FoamNamedValueData { Name = "rhoInf", Value = "1" },
                        new FoamNamedValueData { Name = "CofR", Value = "(0.72 0 0)" },
                        new FoamNamedValueData { Name = "liftDir", Value = "(0 0 1)" },
                        new FoamNamedValueData { Name = "dragDir", Value = "(1 0 0)" },
                        new FoamNamedValueData { Name = "magUInf", Value = "20" },
                        new FoamNamedValueData { Name = "lRef", Value = "1.42" },
                        new FoamNamedValueData { Name = "Aref", Value = "0.75" }
                    ]
                }
            ]
        };
    }

    #endregion

    #region Following Tests

    [Test]
    public void ASweptReferenceValueReachesTheReMeasuredForceTest()
    {
        // Before: the response kept lRef 1.42 and magUInf 20 as offered, and
        // the node re-measured the coefficients with them in every variant.
        var instantiated = Instantiate(TEMPLATED, "25", "1.5");
        var notes = new List<string>();

        var followed = FoamResponseReferences.Follow(Request("forceCoeffs1_sweep"),
            FoamCaseFunctionObjects.Enumerate(TEMPLATED), TEMPLATED,
            FoamCaseFunctionObjects.Enumerate(instantiated), instantiated, notes);

        var parameters = followed!.Responses[0].Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value);
        Assert.That(parameters["lRef"], Is.EqualTo("1.5"), "the token's value");
        Assert.That(parameters["magUInf"], Is.EqualTo("25"), "a reference to a top-level entry the study sweeps");
        Assert.That(parameters["Aref"], Is.EqualTo("0.75"), "not swept: what the request says");
        Assert.That(parameters["CofR"], Is.EqualTo("(0.72 0 0)"));
        Assert.That(notes, Is.EqualTo(new[]
        {
            "Response 'forceCoeffs1_sweep': magUInf follows the case's forceCoeffs1 (system/controlDict:11), which the study sweeps - 25 in this variant.",
            "Response 'forceCoeffs1_sweep': lRef follows the case's forceCoeffs1 (system/controlDict:11), which the study sweeps - 1.5 in this variant."
        }), "the note points at the entry's line, not the block's");
    }

    [Test]
    public void TheNoteNamesTheEntryInTheFileThatHoldsItTest()
    {
        // motorBike's shape: the function object in its own file the
        // controlDict #includes, and another the case ships under system/
        // for an #includeFunc. Before, the note named the block's line of the
        // included file and the #includeFunc's line of the controlDict.
        var files = new Dictionary<string, string>
        {
            ["system/forceCoeffs"] = "forceCoeffs1\n{\n    type forceCoeffs;\n    libs (forces);\n    patches (body);\n    rho rhoInf;\n    rhoInf 1;\n    magUInf 20;\n    lRef {{oc2}};\n    Aref 0.75;\n    CofR (0 0 0);\n}\n",
            ["system/coeffs"] = "type forceCoeffs;\nlibs (forces);\npatches (body);\nrho rhoInf;\nrhoInf 1;\nmagUInf {{oc1}};\nlRef 1;\nAref 1;\nCofR (0 0 0);\n"
        };
        var templated = HEAD + "functions\n{\n    #include \"forceCoeffs\"\n    #includeFunc coeffs\n}\n";
        var instantiated = Instantiate(templated, "25", "1.5");
        var variantFiles = files.ToDictionary(file => file.Key, file => Instantiate(file.Value, "25", "1.5"));
        var request = Request("forceCoeffs1_sweep");
        request.Responses.Add(Request("coeffs").Responses[0]);
        var notes = new List<string>();

        FoamResponseReferences.Follow(request,
            FoamCaseFunctionObjects.Enumerate(templated, path => files.GetValueOrDefault(path)), templated,
            FoamCaseFunctionObjects.Enumerate(instantiated, path => variantFiles.GetValueOrDefault(path)), instantiated, notes);

        Assert.That(notes, Is.EqualTo(new[]
        {
            "Response 'forceCoeffs1_sweep': lRef follows the case's forceCoeffs1 (system/forceCoeffs:9), which the study sweeps - 1.5 in this variant.",
            "Response 'coeffs': magUInf follows the case's coeffs (system/coeffs:6), which the study sweeps - 25 in this variant."
        }));
    }

    [Test]
    public void AParameterTheResponseDidNotCarryIsAddedWhenSweptTest()
    {
        var request = Request("forceCoeffs1");
        request.Responses[0].Parameters.RemoveAll(parameter => parameter.Name == "lRef");
        var instantiated = Instantiate(TEMPLATED, "20", "2");

        var followed = FoamResponseReferences.Follow(request, FoamCaseFunctionObjects.Enumerate(TEMPLATED), TEMPLATED, FoamCaseFunctionObjects.Enumerate(instantiated), instantiated);

        Assert.That(followed!.Responses[0].Parameters.Single(parameter => parameter.Name == "lRef").Value, Is.EqualTo("2"));
        Assert.That(request.Responses[0].Parameters.Any(parameter => parameter.Name == "lRef"), Is.False, "the given request is left as it is");
    }

    [Test]
    public void AResponseThatIsNotTheCasesOwnIsLeftAloneTest()
    {
        var instantiated = Instantiate(TEMPLATED, "25", "1.5");
        var functions = FoamCaseFunctionObjects.Enumerate(TEMPLATED);
        var other = Request("drag");
        var forces = Request("forceCoeffs1");
        forces.Responses[0].Kind = FoamResponseKind.Forces;

        Assert.That(FoamResponseReferences.ReMeasured(other.Responses[0], functions), Is.Null, "another name");
        Assert.That(FoamResponseReferences.ReMeasured(forces.Responses[0], functions), Is.Null, "the case's is a forceCoeffs, the response a forces");
        Assert.That(FoamResponseReferences.ReMeasured(Request("forceCoeffs1_sweep").Responses[0], functions)?.Name, Is.EqualTo("forceCoeffs1"));
        Assert.That(FoamResponseReferences.Follow(other, functions, TEMPLATED, FoamCaseFunctionObjects.Enumerate(instantiated), instantiated)!.Responses[0].Parameters.Single(parameter => parameter.Name == "lRef").Value, Is.EqualTo("1.42"));
        Assert.That(FoamResponseReferences.Follow(null, functions, TEMPLATED, functions, TEMPLATED), Is.Null);
    }

    [Test]
    public void AReferenceIsFollowedThroughTheBodyAndTheControlDictTest()
    {
        var text = HEAD + "L 2;\nfunctions\n{\n    f { type forces; CofR $centre; centre (1 2 3); lRef $L; loop $loop; }\n}\n";
        var function = FoamCaseFunctionObjects.Enumerate(text)[0];

        Assert.That(FoamResponseReferences.Resolve(function, "CofR", text), Is.EqualTo("(1 2 3)"), "the body first");
        Assert.That(FoamResponseReferences.Resolve(function, "lRef", text), Is.EqualTo("2"), "then the controlDict's top level");
        Assert.That(FoamResponseReferences.Resolve(function, "loop", text), Is.Null, "a reference that never ends");
        Assert.That(FoamResponseReferences.Resolve(function, "Aref", text), Is.Null, "absent");
    }

    #endregion
}
