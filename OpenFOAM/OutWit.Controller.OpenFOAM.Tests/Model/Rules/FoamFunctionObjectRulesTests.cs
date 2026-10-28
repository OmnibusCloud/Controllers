using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// A pool node is another person's machine: a case may carry only the
/// function objects that measure and write inside it. Measured on v2606,
/// 2026-10-28: with the kit as it ships, a systemCall ran a command of the
/// case's and a timeActivatedFileUpdate copied a file of the node into the
/// case, both past every rule and with exit 0.
/// </summary>
[TestFixture]
public class FoamFunctionObjectRulesTests
{
    #region Constants

    private const string HEAD = "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 10;\n";

    #endregion

    #region Tools

    private static List<string> Inspect(string functions)
    {
        var findings = new List<string>();
        FoamFunctionObjectRules.Inspect(HEAD + functions, "system/controlDict", findings);
        return findings;
    }

    #endregion

    #region Type Tests

    [Test]
    public void TheMeasurementsACaseWritesAreAllowedTest()
    {
        var functions =
            "functions\n{\n" +
            "    forces1 { type forces; libs (forces); patches (body); CofR (0 0 0); }\n" +
            "    probes1 { type probes; libs (sampling); fields (p U); probeLocations ((0.1 0 0)); }\n" +
            "    minMax { type fieldMinMax; libs (fieldFunctionObjects); fields (p); }\n" +
            "    inlet { type surfaceFieldValue; libs (fieldFunctionObjects); regionType patch; name inlet; operation areaAverage; fields (p); }\n" +
            "    yPlus1 { type yPlus; libs (fieldFunctionObjects); writeControl writeTime; }\n" +
            "}\n";

        Assert.That(Inspect(functions), Is.Empty);
    }

    [Test]
    public void ASystemCallIsRefusedWithItsLineAndWhatItDoesTest()
    {
        var functions =
            "functions\n{\n" +
            "    marker\n    {\n        type            systemCall;\n        libs            (utilityFunctionObjects);\n" +
            "        endCalls        (\"cmd /c echo ran > marker.txt\");\n    }\n}\n";

        var findings = Inspect(functions);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "system/controlDict:7: the function object 'marker' is of type 'systemCall', which runs commands on the node; a case may carry only measurements and writers that stay inside it."
        }));
    }

    [Test]
    public void AFileUpdateFromTheNodeIsRefusedTwiceOverTest()
    {
        // The type reaches the node, and so does the path it names: the
        // type is enough for the refusal, the path is not looked at.
        var functions =
            "functions\n{\n" +
            "    pull\n    {\n        type            timeActivatedFileUpdate;\n        libs            (utilityFunctionObjects);\n" +
            "        fileToUpdate    \"$FOAM_CASE/constant/pulledIn\";\n        timeVsFile      ( ( -1 \"C:/Users/someone/.ssh/id_rsa\" ) );\n    }\n}\n";

        var findings = Inspect(functions);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("system/controlDict:7:").And.Contain("'timeActivatedFileUpdate', which copies a file of the node into the case"));
    }

    [Test]
    public void AnUnknownTypeIsRefusedAsNotOneAPoolNodeRunsTest()
    {
        var findings = Inspect("functions\n{\n    f1 { type myOwnThing; libs (fieldFunctionObjects); }\n}\n");

        Assert.That(findings, Is.EqualTo(new[]
        {
            "system/controlDict:6: the function object 'f1' is of type 'myOwnThing', which is not among the function objects a pool node runs; a case may carry only measurements and writers that stay inside it."
        }));
    }

    [Test]
    public void AFunctionObjectWithoutATypeIsRefusedTest()
    {
        var findings = Inspect("functions\n{\n    f1 { libs (fieldFunctionObjects); fields (p); }\n}\n");

        Assert.That(findings, Is.EqualTo(new[] { "system/controlDict:6: the function object 'f1' names no type." }));
    }

    [Test]
    public void EveryAllowedTypeIsKnownAndNoRefusedOneIsAllowedTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in new[] { "systemCall", "timeActivatedFileUpdate", "writeDictionary", "externalCoupled", "abort", "coded" })
                Assert.That(FoamFunctionObjectRules.IsAllowed(type), Is.False, type);

            foreach (var type in new[] { "forces", "forceCoeffs", "probes", "surfaceFieldValue", "volFieldValue", "fieldMinMax" })
                Assert.That(FoamFunctionObjectRules.IsAllowed(type), Is.True, type);

            Assert.That(FoamFunctionObjectRules.ALLOWED, Is.Unique);
        });
    }

    #endregion

    #region Path Tests

    [Test]
    public void AnAllowedWriterNamingAPathOutsideTheCaseIsRefusedTest()
    {
        var functions =
            "functions\n{\n" +
            "    vtk\n    {\n        type            vtkWrite;\n        libs            (utilityFunctionObjects);\n" +
            "        directory       \"/home/someone/out\";\n        fields          (p);\n    }\n" +
            "    sets1 { type sets; libs (sampling); setFormat raw; sets ( line { type uniform; axis x; start (0 0 0); end (1 0 0); nPoints 10; } ); fields (p); }\n" +
            "}\n";

        var findings = Inspect(functions);

        Assert.That(findings, Is.EqualTo(new[]
        {
            "system/controlDict:7: the function object 'vtk' names \"/home/someone/out\", which reaches outside the case."
        }));
    }

    [Test]
    public void PathsInsideTheCaseAndCaseRootTagsAreNoFindingTest()
    {
        var functions =
            "functions\n{\n" +
            "    vtk { type vtkWrite; libs (utilityFunctionObjects); directory \"<case>/VTK\"; fields (p); }\n" +
            "    ens { type ensightWrite; libs (utilityFunctionObjects); directory \"$FOAM_CASE/postProcessing/ensight\"; fields (U); }\n" +
            "    objs { type writeObjects; libs (utilityFunctionObjects); objects (p); }\n" +
            "}\n";

        Assert.That(Inspect(functions), Is.Empty);
    }

    #endregion

    #region Shape Tests

    [Test]
    public void OnlyTheFunctionsDictionaryIsReadAndABoundaryTypeIsNotAFunctionObjectTest()
    {
        // A "type" elsewhere in the controlDict (a writer's own sub-entry, a
        // DebugSwitches block) is not a function object.
        var text = HEAD + "DebugSwitches { type 1; }\nfunctions\n{\n    forces1 { type forces; libs (forces); patches (body); CofR (0 0 0); }\n}\n";
        var findings = new List<string>();

        FoamFunctionObjectRules.Inspect(text, "system/controlDict", findings);

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void AControlDictWithoutFunctionsHasNoFindingTest()
    {
        Assert.That(Inspect(""), Is.Empty);
    }

    [Test]
    public void AFunctionsEntryThatIsNotABlockIsRefusedByLineTest()
    {
        // What such an entry pulls in cannot be read here, and the
        // controller's includeFunc cannot add a response to it (K5).
        const string sentence = "system/controlDict:4: 'functions' is not a block: what it pulls in cannot be read here, and the controller cannot add a response to it; write it as functions { ... }.";

        Assert.That(Inspect("functions #includeEtc \"caseDicts/postProcessing/fields/CourantNo\";\n"), Is.EqualTo(new[] { sentence }));
        Assert.That(Inspect("functions $myFunctions;\n"), Is.EqualTo(new[] { sentence }));
        Assert.That(Inspect("functions\n{\n    #includeEtc \"caseDicts/postProcessing/fields/CourantNo\"\n}\n"), Is.Empty, "an include inside the block is a block");
    }

    [Test]
    public void AFunctionObjectTheCaseIncludesFromItsOwnSystemIsJudgedLikeABlockTest()
    {
        // v2606 resolves #includeFunc <name> to system/<name> before the kit's
        // etc/caseDicts (functionObjectList::findDict): a case dictionary of a
        // refused type reached the node past the block rule.
        var files = new Dictionary<string, string>
        {
            ["system/marker"] = "type systemCall;\nlibs (utilityFunctionObjects);\nendCalls (\"cmd /c echo ran > marker.txt\");\n",
            ["system/wakeProbes"] = "type probes;\nlibs (sampling);\nfields (p);\nprobeLocations ((0.1 0 0));\n",
            ["system/extra"] = "pull { type timeActivatedFileUpdate; fileToUpdate \"x\"; timeVsFile ((0 \"y\")); }\nminMax { type fieldMinMax; fields (p); }\n"
        };
        var functions = "functions\n{\n    #includeFunc marker\n    #includeFunc wakeProbes\n    #include \"extra\"\n    #includeFunc CourantNo\n    #includeFunc ObukhovLength\n}\n";
        var findings = new List<string>();

        FoamFunctionObjectRules.Inspect(HEAD + functions, "system/controlDict", findings, path => files.GetValueOrDefault(path));

        Assert.That(findings, Is.EqualTo(new[]
        {
            "system/controlDict:6: the function object 'marker (system/marker)' is of type 'systemCall', which runs commands on the node; a case may carry only measurements and writers that stay inside it.",
            "system/extra:1: the function object 'pull' is of type 'timeActivatedFileUpdate', which copies a file of the node into the case; a case may carry only measurements and writers that stay inside it."
        }), "the kit's own dictionaries (CourantNo, ObukhovLength) pass; the case's probes and min/max pass");
    }

    [Test]
    public void TheContentRulesReadTheIncludedFilesOfTheCaseTest()
    {
        var controlDict = HEAD + "functions\n{\n    #includeFunc marker\n}\n";
        var findings = FoamCaseContentRules.Inspect([("system/controlDict", controlDict), ("system/marker", "type systemCall;\nendCalls (\"ls\");\n")]);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("system/controlDict:6:").And.Contain("'marker (system/marker)' is of type 'systemCall'"));
    }

    [Test]
    public void TheRuleRunsThroughTheContentRulesForTheControlDictAloneTest()
    {
        var controlDict = HEAD + "functions\n{\n    marker { type systemCall; libs (utilityFunctionObjects); endCalls (\"ls\"); }\n}\n";
        var elsewhere = "functions\n{\n    marker { type systemCall; }\n}\n";

        var findings = FoamCaseContentRules.Inspect([("system/controlDict", controlDict), ("system/notes", elsewhere), ("0/U", "internalField uniform (1 0 0);\n")]);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("system/controlDict:").And.Contain("systemCall"));
    }

    #endregion
}
