using System.Xml.Linq;

namespace OutWit.Controller.AI.Smt.Tests;

/// <summary>
/// The settings every run of these tests uses (<c>smt.runsettings</c>, named by the project): the
/// adapter's limit of selected tests stands far above the size of the assembly, so that a run that
/// selects tests by category runs those and no other, and the run sees the settings' own
/// parameter. Without the file the adapter runs the whole assembly once a filter selects more than
/// 2,000 tests, and the slice's and the load test's tests run, and fail, in a job that has not
/// what they need.
/// </summary>
[TestFixture]
public sealed class SmtRunSettingsTests
{
    #region Constants

    private const string FILE = "smt.runsettings";
    private const string PARAMETER = "smt.runsettings";
    private const int LEAST_LIMIT = 100_000;

    #endregion

    #region Tests

    [Test]
    public void ThisRunUsesTheProjectsSettingsTest()
    {
        Assert.That(TestContext.Parameters.Get(PARAMETER), Is.EqualTo("used"),
            $"the run was not given {FILE}: the project names it (RunSettingsFilePath); a run with other settings may run the whole assembly whatever its filter");
    }

    [Test]
    public void TheSelectLimitStandsFarAboveTheAssemblyTest()
    {
        var project = Path.Combine(Mock.SmtRigModules.SolutionRoot()!, "AI", "OutWit.Controller.AI.Smt.Tests");
        var settings = XDocument.Load(Path.Combine(project, FILE));
        var limit = int.Parse(settings.Root!.Element("NUnit")!.Element("AssemblySelectLimit")!.Value);
        var tests = typeof(SmtRunSettingsTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Sum(method => method.GetCustomAttributes(inherit: false).Count(attribute => attribute.GetType().Name is "TestAttribute" or "TestCaseAttribute"));

        TestContext.Out.WriteLine($"{tests} test methods and cases written in the assembly (sources of cases not counted); the limit is {limit}");
        Assert.Multiple(() =>
        {
            Assert.That(limit, Is.GreaterThanOrEqualTo(LEAST_LIMIT));
            Assert.That(limit, Is.GreaterThan(10 * tests), "far above the assembly");
            Assert.That(File.ReadAllText(Path.Combine(project, "OutWit.Controller.AI.Smt.Tests.csproj")), Does.Contain("<RunSettingsFilePath>").And.Contain(FILE), "the project names the file");
        });
    }

    #endregion
}
