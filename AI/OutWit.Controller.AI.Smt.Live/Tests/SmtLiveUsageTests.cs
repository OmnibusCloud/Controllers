using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests;

/// <summary>
/// What the usage and the README tell an operator before a run: what 0 means for each option
/// (for <c>--rate</c> it turns the copies off; it is not the controller's default there), and
/// where the key may go.
/// </summary>
[TestFixture]
public sealed class SmtLiveUsageTests
{
    #region Constants

    private const string README = "AI/OutWit.Controller.AI.Smt.Live/README.md";

    #endregion

    #region Tests

    [TestCase("--query", "absent: check")]
    [TestCase("--rate", "0: no package on two machines")]
    [TestCase("--rate", "absent: 0.05")]
    [TestCase("--rlimit", "0 or absent: the profile's, 150000000")]
    [TestCase("--hard-cap", "0 or absent: 60")]
    [TestCase("--memory", "0 or absent: 1024")]
    [TestCase("--seed", "0 or absent: 0")]
    [TestCase("--max-models", "0 or absent: 2")]
    public void TheUsageSaysWhatZeroAndAbsentMeanForEachOptionTest(string option, string meaning)
    {
        var line = Usage().Split('\n').Single(text => text.TrimStart().StartsWith(option + " ", StringComparison.Ordinal));

        Assert.That(line, Does.Contain(meaning));
    }

    [Test]
    public void TheUsageAndTheReadmeSayWhereTheKeyMayGoTest()
    {
        var readme = Readme();

        Assert.Multiple(() =>
        {
            Assert.That(Usage(), Does.Contain("the key goes only to an https:// address, or to this machine (http://localhost) for a local test"));
            Assert.That(readme, Does.Contain("The key goes only to an `https://` address"));
        });
    }

    [Test]
    public void TheReadmeSaysThatZeroTurnsTheCopiesOffTest()
    {
        var readme = Readme();

        Assert.That(readme, Does.Contain("`--rate 0` turns the copies off"));
        Assert.That(readme, Does.Not.Contain("an option not given, or 0, is the controller's default"));
    }

    #endregion

    #region Tools

    /// <summary>The README with every run of white space as one space: a sentence may wrap anywhere.</summary>
    private static string Readme()
    {
        return Regex.Replace(File.ReadAllText(Path.Combine(SmtLiveFixtures.RepositoryRoot(), README)), "[ \t\r\n]+", " ");
    }

    private static string Usage()
    {
        using var output = new StringWriter();
        SmtLiveUsage.Write(output);
        return output.ToString().Replace("\r\n", "\n");
    }

    #endregion
}
