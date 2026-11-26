using System.Reflection;
using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests;

/// <summary>
/// The exit codes: one meaning each, every one of them in the usage and in the README's table;
/// and Ctrl+C, which stops the tool's work at the first press and ends the tool at the second.
/// </summary>
[TestFixture]
public sealed class SmtLiveExitCodesTests
{
    #region Constants

    private const string README = "AI/OutWit.Controller.AI.Smt.Live/README.md";

    #endregion

    #region Code Tests

    [Test]
    public void EveryExitCodeHasAValueOfItsOwnTest()
    {
        var codes = Codes();

        Assert.That(codes.Select(code => code.Value), Is.Unique);
        Assert.That(codes.Select(code => code.Name), Is.EquivalentTo(new[] { "OK", "FAILED", "USAGE", "ALARM", "DIFFER", "STOPPED", "ERROR" }));
    }

    [Test]
    public void TheUsageNamesEveryExitCodeOnceTest()
    {
        using var output = new StringWriter();
        SmtLiveUsage.Write(output);
        var exit = output.ToString()[output.ToString().IndexOf("exit:", StringComparison.Ordinal)..];

        Assert.Multiple(() =>
        {
            foreach (var (name, value) in Codes())
                Assert.That(Regex.Matches(exit, $@"(^|\s){value} ").Count, Is.EqualTo(1), $"{name} ({value}) in the usage");
        });
    }

    [Test]
    public void TheReadmeNamesEveryExitCodeInItsTableTest()
    {
        var readme = File.ReadAllText(Path.Combine(SmtLiveFixtures.RepositoryRoot(), README));
        var table = readme[readme.IndexOf("## Exit codes", StringComparison.Ordinal)..];

        Assert.Multiple(() =>
        {
            foreach (var (name, value) in Codes())
                Assert.That(Regex.IsMatch(table, $@"(?m)^\| {value} \| \S"), Is.True, $"{name} ({value}) in the README's table");
        });
    }

    #endregion

    #region Interrupt Tests

    [Test]
    public void TheFirstCtrlCStopsTheWorkAndTheSecondEndsTheToolTest()
    {
        using var interrupt = new SmtLiveInterrupt();

        var first = interrupt.Press();
        var stopped = interrupt.Token.IsCancellationRequested;
        var second = interrupt.Press();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True, "the tool goes on after the first press, to say what it stopped");
            Assert.That(stopped, Is.True, "the first press stops the work");
            Assert.That(second, Is.False, "the second press ends the tool");
        });
    }

    #endregion

    #region Tools

    private static List<(string Name, int Value)> Codes()
    {
        return typeof(SmtLiveExitCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (field.Name, (int)field.GetRawConstantValue()!))
            .ToList();
    }

    #endregion
}
