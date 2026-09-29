using System.Text.RegularExpressions;
using OutWit.Controller.OpenFOAM.Model;
using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Model.Rules;

/// <summary>
/// The supported-inputs document is held to the rules it publishes: the
/// allow-list and its parallel column, the built-in steps, the forbidden
/// flags, the step limit, the response kinds, the solver classes and the
/// limits the controller applies; and the document and both READMEs carry
/// the trademark statement word for word.
/// </summary>
[TestFixture]
public class FoamSupportedInputsDocumentTests
{
    private const string STATEMENT =
        "OpenFOAM® is a registered trademark of OpenCFD Limited. This offering is not approved or endorsed by OpenCFD Limited, " +
        "producer and distributor of the OpenFOAM software via www.openfoam.com, and owner of the OPENFOAM® and OpenCFD® trade marks.";

    private static readonly Regex CODE = new("`([^`]+)`", RegexOptions.Compiled);

    private string m_openFoamRoot = null!;

    private string m_document = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var solutionRoot = OpenFOAMTestPaths.FindSolutionRoot();
        if (solutionRoot == null)
            Assert.Ignore("Solution root not found");

        m_openFoamRoot = Path.Combine(solutionRoot, "OpenFOAM");
        m_document = File.ReadAllText(Path.Combine(m_openFoamRoot, "SUPPORTED-INPUTS.md")).Replace("\r\n", "\n");
    }

    #region Tools

    /// <summary>The lines of a section: from its heading to the next heading of any level.</summary>
    private List<string> Section(string heading)
    {
        var lines = m_document.Split('\n');
        var start = Array.IndexOf(lines, heading);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"the document has no '{heading}'");

        return lines.Skip(start + 1).TakeWhile(line => !line.StartsWith('#')).ToList();
    }

    /// <summary>The cells of a section's table rows, header and separator left out.</summary>
    private List<string[]> Rows(string heading)
    {
        return Section(heading)
            .Where(line => line.StartsWith('|'))
            .Skip(2)
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToList();
    }

    private static List<string> CodeIn(string text)
    {
        return CODE.Matches(text).Select(match => match.Groups[1].Value).ToList();
    }

    private static string Flat(string text)
    {
        return Regex.Replace(text, @"\s+", " ");
    }

    #endregion

    #region Rule Tests

    [Test]
    public void TheUtilitiesTableIsTheAllowListTest()
    {
        var rows = Rows("### Utilities");

        Assert.That(rows.Select(row => CodeIn(row[0]).Single()), Is.EqualTo(FoamAllowList.UTILITIES));
        Assert.That(rows.Where(row => row[1] == "yes").Select(row => CodeIn(row[0]).Single()), Is.EquivalentTo(FoamAllowList.PARALLEL_CAPABLE_UTILITIES));
    }

    [Test]
    public void TheBuiltInStepsAndTheForbiddenFlagsAreTheRulesTest()
    {
        var builtIn = Section("### Built-in steps").Where(line => line.StartsWith("- ")).Select(line => CodeIn(line)[0].Split(' ')[0]);
        Assert.That(builtIn, Is.EqualTo(FoamAllowList.BUILT_IN_STEPS));
        Assert.That(m_document, Does.Contain($"`{FoamAllowList.RESTORE_INITIAL_FIELDS} {FoamAllowList.PROCESSOR_FORM}`"));
        Assert.That(m_document, Does.Contain($"`{FoamAllowList.INCLUDE_FUNCTION} <response>`"));
        Assert.That(m_document, Does.Contain($"`#{FoamAllowList.INCLUDE_FUNCTION} <response>`"), "the one line the step writes, said where the case's files are said to travel as they are");

        var arguments = Section("### Arguments");
        var flags = arguments[arguments.FindIndex(line => line.EndsWith("refused in a recipe:")) + 2];
        Assert.That(CodeIn(flags), Is.EqualTo(FoamAllowList.FORBIDDEN_FLAGS));
    }

    [Test]
    public void TheLimitsAreTheControllersTest()
    {
        Assert.That(Flat(m_document), Does.Contain($"at most **{FoamRecipeRules.MAX_STEPS}**"));
        Assert.That(Flat(m_document), Does.Contain($"at most {FoamDecomposition.MAX_DEFAULT_RANKS} ranks"));
        Assert.That(Flat(m_document), Does.Contain($"larger than {FoamCaseContentRules.MAX_SCANNED_BYTES / (1024 * 1024)} MB"));
    }

    [Test]
    public void TheResponseKindsAreTheVocabularyTest()
    {
        Assert.That(Rows("## Responses").Select(row => CodeIn(row[0]).Single()), Is.EqualTo(Enum.GetNames<FoamResponseKind>()));
    }

    [Test]
    public void TheSolverClassesAreTheClassMapTest()
    {
        var published = Rows("## Solver classes")
            .SelectMany(row => CodeIn(row[1]).Select(application => (Application: application, Class: CodeIn(row[0]).Single())))
            .ToDictionary(pair => pair.Application, pair => pair.Class);

        Assert.That(Rows("## Solver classes").Select(row => CodeIn(row[0]).Single()), Is.EqualTo(FoamSolverClasses.ALL));
        Assert.That(published, Is.EquivalentTo(FoamSolverClasses.APPLICATIONS));
    }

    #endregion

    #region Notice Tests

    [TestCase("SUPPORTED-INPUTS.md")]
    [TestCase("OutWit.Controller.OpenFOAM/README.md")]
    [TestCase("OutWit.Controller.OpenFOAM.Model/README.md")]
    public void EveryPageCarriesTheTrademarkStatementTest(string page)
    {
        Assert.That(Flat(File.ReadAllText(Path.Combine(m_openFoamRoot, page))), Does.Contain(STATEMENT));
    }

    #endregion
}
