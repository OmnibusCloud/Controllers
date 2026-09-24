using OutWit.Controller.OpenFOAM.Recipes;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Recipes;

[TestFixture]
public class FoamCaseInspectorTests
{
    private string m_case = null!;

    [SetUp]
    public void Setup()
    {
        m_case = OpenFOAMTestPaths.CreateScratch("foam-case");
        Write("system/controlDict", "FoamFile { object controlDict; }\napplication simpleFoam;\nstartTime 0;\n");
        Write("system/fvSchemes", "ddtSchemes { default steadyState; }\n");
        Write("constant/transportProperties", "nu 1e-05;\n");
        Write("0/U", "internalField uniform (10 0 0);\n");
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

    #region Inspection Tests

    [Test]
    public void APlainCaseHasNoFindingsTest()
    {
        Assert.That(FoamCaseInspector.Inspect(m_case), Is.Empty);
        Assert.That(FoamCaseInspector.ReadApplication(m_case), Is.EqualTo("simpleFoam"));
    }

    [Test]
    public void CodeStreamIsRefusedWithFileAndLineTest()
    {
        Write("0/p", "internalField #codeStream\n{\n    code #{ os << 0; #};\n};\n");

        var findings = FoamCaseInspector.Inspect(m_case);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("0/p:1:").And.Contain("codeStream"));
    }

    [Test]
    public void CalcAndCodedEntriesAreRefusedButEvalIsNotTest()
    {
        Write("system/blockMeshDict", "x 1;\ny #eval{ $x * 2 };\nz #calc \"$x * 3\";\n");
        Write("0/T", "boundaryField { wall { type codedFixedValue; } }\n");
        Write("system/functions", "f1 { type coded; name f1; }\n");

        var findings = FoamCaseInspector.Inspect(m_case);

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.StartsWith("system/blockMeshDict:3:").And.Some.Contains("#calc"));
        Assert.That(findings, Has.Some.StartsWith("0/T:1:"));
        Assert.That(findings, Has.Some.StartsWith("system/functions:1:"));
    }

    [Test]
    public void CommentedOutCodeIsNotAFindingTest()
    {
        Write("system/fvSolution", "// codeStream was here once\n/* #calc \"x\" */\nsolvers { }\n");

        Assert.That(FoamCaseInspector.Inspect(m_case), Is.Empty);
    }

    [Test]
    public void LibrariesOutsideTheKitAndIncludesOutsideTheCaseAreRefusedTest()
    {
        Write("system/controlDict", "application simpleFoam;\nlibs (\"libmyBCs.so\" \"libfieldFunctionObjects.so\");\n#include \"/home/user/common\"\n#include \"$FOAM_CASE/system/local\"\n#includeEtc \"caseDicts/setConstraintTypes\"\n#include \"../shared/dict\"\n");

        var findings = FoamCaseInspector.Inspect(m_case, library => library.Contains("fieldFunctionObjects", StringComparison.Ordinal));

        Assert.That(findings, Has.Count.EqualTo(3));
        Assert.That(findings, Has.Some.Contains("libmyBCs.so"));
        Assert.That(findings, Has.Some.Contains("/home/user/common"));
        Assert.That(findings, Has.Some.Contains("../shared/dict"));
    }

    [Test]
    public void EveryIncludeDirectiveIsCheckedAndTheCasePrefixDoesNotHideAnEscapeTest()
    {
        Write("system/fvSolution",
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
            "solvers { }\n");

        var findings = FoamCaseInspector.Inspect(m_case);

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
        Assert.That(FoamCaseInspector.StaysInsideTheCase("system/local"), Is.True);
        Assert.That(FoamCaseInspector.StaysInsideTheCase("$FOAM_CASE/system/local"), Is.True);
        Assert.That(FoamCaseInspector.StaysInsideTheCase("<case>/0/U"), Is.True);
        Assert.That(FoamCaseInspector.StaysInsideTheCase("$FOAM_CASE/../x"), Is.False);
        Assert.That(FoamCaseInspector.StaysInsideTheCase("$FOAM_CASE"), Is.False, "the bare variable is not a file");
        Assert.That(FoamCaseInspector.StaysInsideTheCase("$FOAM_CASE/$WM_PROJECT_DIR/x"), Is.False);
        Assert.That(FoamCaseInspector.StaysInsideTheCase("/abs/x"), Is.False);
        Assert.That(FoamCaseInspector.StaysInsideTheCase("C:/x"), Is.False);
        Assert.That(FoamCaseInspector.StaysInsideTheCase(string.Empty), Is.False);
    }

    [Test]
    public void ADecomposedOnlyCaseAndAMissingApplicationAreRefusedTest()
    {
        Directory.Delete(Path.Combine(m_case, "0"), recursive: true);
        Directory.CreateDirectory(Path.Combine(m_case, "processor0", "0"));
        Directory.CreateDirectory(Path.Combine(m_case, "processor1", "0"));
        Write("system/controlDict", "startTime 0;\n");

        var findings = FoamCaseInspector.Inspect(m_case);

        Assert.That(findings, Has.Some.Contains("decomposed only"));
        Assert.That(findings, Has.Some.Contains("no 'application' entry"));
    }

    [Test]
    public void ADynamicCodeDirectoryIsRefusedTest()
    {
        Directory.CreateDirectory(Path.Combine(m_case, "dynamicCode", "x"));

        Assert.That(FoamCaseInspector.Inspect(m_case), Has.Some.StartsWith("dynamicCode/"));
    }

    #endregion
}
