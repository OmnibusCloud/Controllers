using OutWit.Controller.OpenFOAM.Model.Rules;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

[TestFixture]
public class FoamCaseContentRulesTests
{
    private const string CONTROL_DICT = "FoamFile { object controlDict; }\napplication simpleFoam;\nstartTime 0;\n";

    #region Tools

    private static List<(string RelativePath, string? Text)> PlainCase()
    {
        return
        [
            ("system/controlDict", CONTROL_DICT),
            ("system/fvSchemes", "ddtSchemes { default steadyState; }\n"),
            ("constant/transportProperties", "nu 1e-05;\n"),
            ("0/U", "internalField uniform (10 0 0);\n")
        ];
    }

    private static List<(string RelativePath, string? Text)> PlainCaseWith(params (string RelativePath, string? Text)[] files)
    {
        var all = PlainCase();
        foreach (var file in files)
        {
            all.RemoveAll(existing => existing.RelativePath == file.RelativePath);
            all.Add(file);
        }

        return all;
    }

    #endregion

    #region Inspection Tests

    [Test]
    public void APlainCaseHasNoFindingsTest()
    {
        Assert.That(FoamCaseContentRules.Inspect(PlainCase()), Is.Empty);
        Assert.That(FoamCaseContentRules.ReadApplication(CONTROL_DICT), Is.EqualTo("simpleFoam"));
    }

    [Test]
    public void CodeStreamIsRefusedWithFileAndLineTest()
    {
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(("0/p", "internalField #codeStream\n{\n    code #{ os << 0; #};\n};\n")));

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("0/p:1:").And.Contain("codeStream"));
    }

    [Test]
    public void CalcAndCodedEntriesAreRefusedButEvalIsNotTest()
    {
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(
            ("system/blockMeshDict", "x 1;\ny #eval{ $x * 2 };\nz #calc \"$x * 3\";\n"),
            ("0/T", "boundaryField { wall { type codedFixedValue; } }\n"),
            ("system/functions", "f1 { type coded; name f1; }\n")));

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.StartsWith("system/blockMeshDict:3:").And.Some.Contains("#calc"));
        Assert.That(findings, Has.Some.StartsWith("0/T:1:"));
        Assert.That(findings, Has.Some.StartsWith("system/functions:1:"));
    }

    [Test]
    public void CommentedOutCodeIsNotAFindingTest()
    {
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(("system/fvSolution", "// codeStream was here once\n/* #calc \"x\" */\nsolvers { }\n")));

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void LibrariesOutsideTheKitAndIncludesOutsideTheCaseAreRefusedTest()
    {
        var controlDict = "application simpleFoam;\nlibs (\"libmyBCs.so\" \"libfieldFunctionObjects.so\");\n#include \"/home/user/common\"\n#include \"$FOAM_CASE/system/local\"\n#includeEtc \"caseDicts/setConstraintTypes\"\n#include \"../shared/dict\"\n";

        var findings = FoamCaseContentRules.Inspect(
            PlainCaseWith(("system/controlDict", controlDict)),
            library => library.Contains("fieldFunctionObjects", StringComparison.Ordinal));

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.Contains("libmyBCs.so"));
        Assert.That(findings, Has.Some.Contains("/home/user/common"));
        Assert.That(findings, Has.Some.Contains("../shared/dict"));
    }

    [Test]
    public void EveryIncludeDirectiveIsCheckedAndTheCasePrefixDoesNotHideAnEscapeTest()
    {
        var fvSolution =
            "#include \"$FOAM_CASE/../shared/solution\"\n" +
            "#include \"${FOAM_CASE}/system/local\"\n" +
            "#sinclude \"../optional/dict\"\n" +
            "#includeIfPresent \"<case>/system/present\"\n" +
            "#includeIfPresent \"<case>/../absent\"\n" +
            "#includeEtc \"../etc/escape\"\n" +
            "#includeEtc \"caseDicts/setConstraintTypes\"\n" +
            "#include \"$HOME/.OpenFOAM/dict\"\n" +
            "#include \"~OpenFOAM/dict\"\n" +
            "#include \"<etc>/caseDicts/x\"\n" +
            "solvers { }\n";

        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(("system/fvSolution", fvSolution)));

        Assert.That(findings, Has.Count.EqualTo(7));
        Assert.That(findings, Has.Some.Contains("$FOAM_CASE/../shared/solution").And.Some.Contains("#include"));
        Assert.That(findings, Has.Some.Contains("../optional/dict").And.Some.Contains("#sinclude"));
        Assert.That(findings, Has.Some.Contains("<case>/../absent").And.Some.Contains("#includeIfPresent"));
        Assert.That(findings, Has.Some.Contains("../etc/escape").And.Some.Contains("#includeEtc"));
        Assert.That(findings, Has.Some.Contains("$HOME/.OpenFOAM/dict"));
        Assert.That(findings, Has.Some.Contains("~OpenFOAM/dict"));
        Assert.That(findings, Has.Some.Contains("<etc>/caseDicts/x"));
        Assert.That(findings, Has.None.Contains("system/local").And.None.Contains("system/present").And.None.Contains("setConstraintTypes"));
    }

    [Test]
    public void TheCaseRuleIsAnsweredForATargetAloneTest()
    {
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("system/local"), Is.True);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("$FOAM_CASE/system/local"), Is.True);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("<case>/0/U"), Is.True);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("$FOAM_CASE/../x"), Is.False);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("$FOAM_CASE"), Is.False, "the bare variable is not a file");
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("$FOAM_CASE/$WM_PROJECT_DIR/x"), Is.False);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("/abs/x"), Is.False);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase("C:/x"), Is.False);
        Assert.That(FoamCaseContentRules.StaysInsideTheCase(string.Empty), Is.False);
    }

    [Test]
    public void ADecomposedOnlyCaseAndAMissingApplicationAreRefusedTest()
    {
        var findings = FoamCaseContentRules.Inspect(
        [
            ("system/controlDict", "startTime 0;\n"),
            ("processor0/0/U", "internalField uniform (0 0 0);\n"),
            ("processor1/0/U", "internalField uniform (0 0 0);\n")
        ]);

        Assert.That(findings, Has.Some.Contains("decomposed only"));
        Assert.That(findings, Has.Some.Contains("no 'application' entry"));
    }

    [Test]
    public void ADecomposedCaseWithItsReconstructedFieldsIsNotRefusedTest()
    {
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(("processor0/0/U", "internalField uniform (0 0 0);\n")));

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void AMissingOrUnreadControlDictIsAFindingTest()
    {
        var missing = FoamCaseContentRules.Inspect([("0/U", "internalField uniform (0 0 0);\n")]);
        var unread = FoamCaseContentRules.Inspect(PlainCaseWith(("system/controlDict", null)));

        Assert.That(missing, Is.EqualTo(new[] { "system/controlDict: missing." }));
        Assert.That(unread, Is.EqualTo(new[] { "system/controlDict: could not be read." }));
    }

    [Test]
    public void AFileInADynamicCodeDirectoryIsRefusedOnceTest()
    {
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(
            ("dynamicCode/fixedValueFoo/code.C", "codeStream\n"),
            ("dynamicCode/fixedValueFoo/Make/files", "x\n")));

        Assert.That(findings, Is.EqualTo(new[] { "dynamicCode/: the case carries compiled run-time code; the kit ships no compiler." }));
    }

    [Test]
    public void FilesOutsideTheScannedSetAreNotReadForRunTimeCodeTest()
    {
        // A mesh, a decomposed copy and an earlier run's output are never
        // dictionaries a run compiles; whatever text they carry is theirs.
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(
            ("constant/polyMesh/boundary", "wall { type codedFixedValue; }\n"),
            ("processor0/constant/polyMesh/boundary", "#codeStream\n"),
            ("postProcessing/probes/0/p", "# codeStream\n")));

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void ABinaryFieldIsInspectedAroundItsBinaryListTest()
    {
        // The entries of a binary field outside its list are dictionary text,
        // and a coded boundary condition there compiles like anywhere else.
        var field = "FoamFile { format binary; }\ninternalField nonuniform List<scalar> 2(\0\u0001ÿ\0\0\0\0\0\u0010é\0\0\0\0\0\0);\nboundaryField { wall { type codedFixedValue; } }\n";

        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(("0/p", field)));

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("0/p:3:"));
    }

    [Test]
    public void AByteOrderMarkIsRefusedByNameTest()
    {
        // OpenFOAM reads the three bytes as the start of the first word:
        // "First token could not be read or is not 'FoamFile'".
        var findings = FoamCaseContentRules.Inspect(PlainCaseWith(("constant/transportProperties", "ï»¿FoamFile { object transportProperties; }\nnu 1e-05;\n")));

        Assert.That(findings, Is.EqualTo(new[] { "constant/transportProperties:1: the file starts with a UTF-8 byte order mark, which OpenFOAM reads as part of the first word; save it without one." }));
    }

    [Test]
    public void TheScannedSetIsDecidedByPathAndSizeTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FoamCaseContentRules.IsScanned("system/controlDict", 100), Is.True);
            Assert.That(FoamCaseContentRules.IsScanned("0.orig/include/initialConditions", 100), Is.True);
            Assert.That(FoamCaseContentRules.IsScanned("Allrun", 100), Is.True);
            Assert.That(FoamCaseContentRules.IsScanned("constant/polyMesh/points", 100), Is.False);
            Assert.That(FoamCaseContentRules.IsScanned("processor0/0/U", 100), Is.False);
            Assert.That(FoamCaseContentRules.IsScanned("postProcessing/probes/0/p", 100), Is.False);
            Assert.That(FoamCaseContentRules.IsScanned("dynamicCode/x/code.C", 100), Is.False);
            Assert.That(FoamCaseContentRules.IsScanned("0/U", FoamCaseContentRules.MAX_SCANNED_BYTES + 1), Is.False);
        });
    }

    [Test]
    public void TheApplicationIsReadPastCommentsTest()
    {
        Assert.That(FoamCaseContentRules.ReadApplication("// application icoFoam;\napplication   pimpleFoam ;\n"), Is.EqualTo("pimpleFoam"));
        Assert.That(FoamCaseContentRules.ReadApplication("/* application icoFoam; */\nstartTime 0;\n"), Is.Null);
    }

    #endregion
}
