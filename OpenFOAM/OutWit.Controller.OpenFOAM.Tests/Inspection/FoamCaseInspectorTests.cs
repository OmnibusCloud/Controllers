using System.Text;
using OutWit.Controller.OpenFOAM.Inspection;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Inspection;

/// <summary>
/// The node's side of the run-time code rules: reading the materialised case
/// the way the rules expect it. The rules themselves are tested in
/// <c>FoamCaseContentRulesTests</c>.
/// </summary>
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
        WriteBytes(relative, Encoding.ASCII.GetBytes(text));
    }

    private void WriteBytes(string relative, byte[] bytes)
    {
        var path = Path.Combine(m_case, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    #endregion

    #region Inspection Tests

    [Test]
    public void APlainCaseHasNoFindingsTest()
    {
        Assert.That(FoamCaseInspector.Inspect(m_case), Is.Empty);
    }

    [Test]
    public void AFindingNamesTheRelativePathWithForwardSlashesAndTheLineTest()
    {
        Write("0/include/p", "x 1;\ninternalField #codeStream { code #{ #}; };\n");

        var findings = FoamCaseInspector.Inspect(m_case);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("0/include/p:2:"));
    }

    [Test]
    public void ABytePastAsciiInACommentDoesNotHideAFindingTest()
    {
        // A Latin-1 comment is not UTF-8; the file is still read and its line counted.
        var head = new byte[] { (byte)'/', (byte)'/', (byte)' ', 0xE9, 0xC3, 0x28, (byte)'\n' };
        WriteBytes("system/fvSolution", head.Concat(Encoding.ASCII.GetBytes("x #calc \"1+1\";\n")).ToArray());

        var findings = FoamCaseInspector.Inspect(m_case);

        Assert.That(findings, Has.Exactly(1).Items);
        Assert.That(findings[0], Does.StartWith("system/fvSolution:2:").And.Contain("#calc"));
    }

    [Test]
    public void TheKitLibraryQuestionIsPassedThroughTest()
    {
        Write("system/controlDict", "application simpleFoam;\nlibs (\"libmyBCs.so\");\n");

        Assert.That(FoamCaseInspector.Inspect(m_case, _ => true), Is.Empty);
        Assert.That(FoamCaseInspector.Inspect(m_case, _ => false), Has.Some.Contains("libmyBCs.so"));
    }

    [Test]
    public void ADecomposedOnlyCaseIsRefusedTest()
    {
        Directory.Delete(Path.Combine(m_case, "0"), recursive: true);
        Write("processor0/0/U", "internalField uniform (0 0 0);\n");
        Write("processor1/0/U", "internalField uniform (0 0 0);\n");

        Assert.That(FoamCaseInspector.Inspect(m_case), Has.Some.Contains("decomposed only"));
    }

    [Test]
    public void AFileUnderDynamicCodeIsRefusedTest()
    {
        Write("dynamicCode/x/code.C", "int main() {}\n");

        Assert.That(FoamCaseInspector.Inspect(m_case), Has.Some.StartsWith("dynamicCode/"));
    }

    #endregion
}
