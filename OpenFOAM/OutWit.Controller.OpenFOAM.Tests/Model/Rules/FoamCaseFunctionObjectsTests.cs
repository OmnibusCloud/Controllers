using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// The function objects a case runs, read as v2606 reads them: the blocks
/// of the last top-level functions dictionary, the files it includes, and
/// every #includeFunc - the case's own system/&lt;name&gt; first, the kit's
/// etc/caseDicts/postProcessing otherwise (functionObjectList::findDict).
/// </summary>
[TestFixture]
public class FoamCaseFunctionObjectsTests
{
    #region Constants

    private const string HEAD = "FoamFile { object controlDict; }\napplication simpleFoam;\nendTime 10;\n";

    #endregion

    #region Enumeration Tests

    [Test]
    public void TheBlocksOfTheFunctionsDictionaryAreEnumeratedInOrderTest()
    {
        var text = HEAD +
                   "functions\n{\n" +
                   "    forces1 { type forces; libs (forces); patches (body); CofR (0 0 0); rho rhoInf; rhoInf 1.2; }\n" +
                   "    // a comment { type systemCall; }\n" +
                   "    probes1\n    {\n        type probes; fields (p U); probeLocations ((0.1 0 0));\n    }\n" +
                   "}\n";

        var functions = FoamCaseFunctionObjects.Enumerate(text);

        Assert.That(functions.Select(function => (function.Name, function.Type, function.Line, function.Source)), Is.EqualTo(new[]
        {
            ("forces1", "forces", 6, FoamCaseFunctionObjectSource.Block),
            ("probes1", "probes", 9, FoamCaseFunctionObjectSource.Block)
        }));
        Assert.That(functions[0].Entry("rhoInf"), Is.EqualTo("1.2"));
        Assert.That(functions[0].Entry("CofR"), Is.EqualTo("(0 0 0)"));
        Assert.That(functions[0].Entry("magUInf"), Is.Null);
        Assert.That(functions[0].RelativePath, Is.EqualTo("system/controlDict"));
    }

    [Test]
    public void AnIncludeFuncIsTheCasesOwnDictionaryFirstAndTheKitsOtherwiseTest()
    {
        var files = new Dictionary<string, string>
        {
            ["system/wake"] = "type probes;\nlibs (sampling);\nfields (p);\nprobeLocations ((0.1 0 0));\n"
        };
        var text = HEAD + "functions\n{\n    #includeFunc wake\n    #includeFunc forceCoeffsIncompressible\n    #includeFunc probes(p, U)\n    #includeFunc ObukhovLength\n}\n";

        var functions = FoamCaseFunctionObjects.Enumerate(text, path => files.GetValueOrDefault(path));

        Assert.That(functions.Select(function => (function.Name, function.Type, function.Source, function.Arguments)), Is.EqualTo(new[]
        {
            ("wake", "probes", FoamCaseFunctionObjectSource.IncludedCaseDictionary, ""),
            ("forceCoeffsIncompressible", "forceCoeffs", FoamCaseFunctionObjectSource.IncludedKitDictionary, ""),
            ("probes", "probes", FoamCaseFunctionObjectSource.IncludedKitDictionary, "p, U"),
            ("ObukhovLength", (string?)null, FoamCaseFunctionObjectSource.IncludedKitDictionary, "")
        }), "a kit dictionary named after an allowed type is that type; one the Model does not know has no type");
        Assert.That(functions[0].RelativePath, Is.EqualTo("system/wake"));
        Assert.That(functions[0].Line, Is.EqualTo(6), "the line of the #includeFunc in the controlDict");
        Assert.That(functions[0].Entry("probeLocations"), Is.EqualTo("((0.1 0 0))"));
        Assert.That(functions[1].RelativePath, Is.EqualTo("etc/caseDicts/postProcessing/forceCoeffsIncompressible"));

        Assert.That(FoamCaseFunctionObjects.Enumerate(text).Select(function => function.Source), Has.All.EqualTo(FoamCaseFunctionObjectSource.IncludedKitDictionary),
            "without a file lookup every #includeFunc counts as the kit's");
    }

    [Test]
    public void TheBlocksOfAnIncludedFileAreEnumeratedWhereTheIncludeStandsTest()
    {
        var files = new Dictionary<string, string>
        {
            ["system/myFunctions"] = "// the case's own functions\nminMax { type fieldMinMax; fields (p); }\nflow { type surfaceFieldValue; name inlet; operation sum; fields (phi); }\n",
            ["myRootFunctions"] = "atRoot { type yPlus; }\n"
        };
        var text = HEAD + "functions\n{\n    first { type forces; patches (a); CofR (0 0 0); }\n    #include \"myFunctions\"\n    #include \"../myRootFunctions\"\n    #include \"missing\"\n    last { type probes; }\n}\n";

        var functions = FoamCaseFunctionObjects.Enumerate(text, path => files.GetValueOrDefault(path));

        Assert.That(functions.Select(function => (function.Name, function.Type, function.RelativePath, function.Line, function.Source)), Is.EqualTo(new[]
        {
            ("first", "forces", "system/controlDict", 6, FoamCaseFunctionObjectSource.Block),
            ("minMax", "fieldMinMax", "system/myFunctions", 2, FoamCaseFunctionObjectSource.IncludedFile),
            ("flow", "surfaceFieldValue", "system/myFunctions", 3, FoamCaseFunctionObjectSource.IncludedFile),
            ("atRoot", "yPlus", "myRootFunctions", 1, FoamCaseFunctionObjectSource.IncludedFile),
            ("last", "probes", "system/controlDict", 10, FoamCaseFunctionObjectSource.Block)
        }));
    }

    [Test]
    public void OnlyTheLastTopLevelFunctionsCountsAndNothingNestedOrQuotedTest()
    {
        var text = HEAD +
                   "DebugSwitches { functions { nested { type systemCall; } } }\n" +
                   "functions { early { type yPlus; } }\n" +
                   "functions\n{\n    late { type probes; comment \"functions { quoted { type systemCall; } }\"; }\n}\n";

        var functions = FoamCaseFunctionObjects.Enumerate(text);

        Assert.That(functions.Select(function => function.Name), Is.EqualTo(new[] { "late" }));
        Assert.That(functions[0].Entry("comment"), Does.StartWith("\"functions"));
    }

    #endregion

    #region Verdict Tests

    [Test]
    public void TheFunctionsEntryIsABlockOrItIsNotTest()
    {
        Assert.That(FoamCaseFunctionObjects.Functions(HEAD), Is.Null);
        Assert.That(FoamCaseFunctionObjects.Functions(HEAD + "functions\n{\n}\n"), Is.EqualTo((4, true)));
        Assert.That(FoamCaseFunctionObjects.Functions(HEAD + "functions #includeEtc \"caseDicts/x\";\n"), Is.EqualTo((4, false)));
        Assert.That(FoamCaseFunctionObjects.Functions(HEAD + "functions $f;\n"), Is.EqualTo((4, false)));
        Assert.That(FoamCaseFunctionObjects.Functions(HEAD + "functions\n{\n    f { type probes; }\n"), Is.EqualTo((4, false)), "never closed");
        Assert.That(FoamCaseFunctionObjects.Enumerate(HEAD + "functions $f;\n"), Is.Empty);
    }

    #endregion

    #region Entry Tests

    [Test]
    public void AnEntryIsReadAsWrittenAtTheTopLevelOfTheBodyTest()
    {
        const string body = "\n    type forceCoeffs;\n    lRef $lRef;\n    Aref 0.75;\n    coordinateSystem { origin (0 0 0); e1 (1 0 0); e3 (0 0 1); }\n    binData { nBin 20; Aref 1; }\n    name \"a; b\";\n";

        Assert.That(FoamCaseFunctionObjects.Entry(body, "lRef"), Is.EqualTo("$lRef"), "a reference stays a reference");
        Assert.That(FoamCaseFunctionObjects.Entry(body, "Aref"), Is.EqualTo("0.75"), "the top-level one, not binData's");
        Assert.That(FoamCaseFunctionObjects.Entry(body, "coordinateSystem"), Is.EqualTo("{ origin (0 0 0); e1 (1 0 0); e3 (0 0 1); }"));
        Assert.That(FoamCaseFunctionObjects.Entry(body, "e1"), Is.Null, "nested");
        Assert.That(FoamCaseFunctionObjects.Entry(body, "name"), Is.EqualTo("\"a; b\""), "a string's semicolon is not the end");
        Assert.That(FoamCaseFunctionObjects.Entry(body, "nBin"), Is.Null);
    }

    #endregion

    #region Path Tests

    [Test]
    public void AnIncludeTargetResolvesAsOpenFoamResolvesItTest()
    {
        Assert.That(FoamCaseFunctionObjects.ResolveCasePath("myFunctions", "system/controlDict"), Is.EqualTo("system/myFunctions"));
        Assert.That(FoamCaseFunctionObjects.ResolveCasePath("../myFunctions", "system/controlDict"), Is.EqualTo("myFunctions"));
        Assert.That(FoamCaseFunctionObjects.ResolveCasePath("<system>/x", "system/controlDict"), Is.EqualTo("system/x"));
        Assert.That(FoamCaseFunctionObjects.ResolveCasePath("$FOAM_CASE/constant/x", "system/controlDict"), Is.EqualTo("constant/x"));
        Assert.That(FoamCaseFunctionObjects.ResolveCasePath("../../x", "system/controlDict"), Is.Null, "outside the case");
        Assert.That(FoamCaseFunctionObjects.ResolveCasePath("/etc/passwd", "system/controlDict"), Is.Null);
    }

    #endregion
}
