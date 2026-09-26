using OutWit.Controller.OpenFOAM.Model.Rules;
using OutWit.Controller.OpenFOAM.Runtime;
using OutWit.Controller.OpenFOAM.Tests.Utils;

namespace OutWit.Controller.OpenFOAM.Tests.Oracle;

/// <summary>
/// The allow-list held to the REAL kit: every utility a recipe may name is
/// an executable of the kit, and every one a parallel step may name takes
/// <c>-parallel</c> on it - so a recipe the rules accept never fails on a
/// node for a missing executable or a refused flag.
/// </summary>
[TestFixture]
[Category("Kit")]
public class FoamKitAllowListOracleTests
{
    #region Fields

    private string m_scratch = null!;

    private FoamKit m_kit = null!;

    #endregion

    [SetUp]
    public void Setup()
    {
        var kitPath = Environment.GetEnvironmentVariable(FoamKitResolver.ENV_KIT_PATH);
        if (string.IsNullOrEmpty(kitPath))
            Assert.Ignore($"{FoamKitResolver.ENV_KIT_PATH} is not set; the kit oracle runs only where a kit is unpacked");

        m_kit = FoamKitResolver.Resolve(typeof(FoamKit).Assembly.Location)
                ?? throw new InvalidOperationException($"{FoamKitResolver.ENV_KIT_PATH}={kitPath} does not resolve to a kit");
        m_scratch = OpenFOAMTestPaths.CreateScratch("foam-oracle-allow-list");
        Directory.CreateDirectory(Path.Combine(m_scratch, "home"));
        Directory.CreateDirectory(Path.Combine(m_scratch, "tmp"));
    }

    [TearDown]
    public void TearDown()
    {
        if (m_scratch != null)
            OpenFOAMTestPaths.TryDelete(m_scratch);
    }

    #region Oracle Tests

    [Test]
    public void EveryAllowListedUtilityIsAnExecutableOfTheKitTest()
    {
        Assert.That(FoamAllowList.UTILITIES.Where(utility => !m_kit.HasExecutable(utility)), Is.Empty);
    }

    [Test]
    public async Task EveryParallelCapableUtilityTakesParallelOnTheKitTest()
    {
        var refusing = new List<string>();
        foreach (var utility in FoamAllowList.PARALLEL_CAPABLE_UTILITIES)
        {
            var log = Path.Combine(m_scratch, $"help.{utility}");
            var outcome = await FoamProcessRunner.RunAsync(m_kit.ExecutablePath(utility), ["-help-full"], m_scratch, m_kit.EnvironmentFor(m_scratch), log);
            var text = File.Exists(log) ? await File.ReadAllTextAsync(log) : string.Empty;
            var takesParallel = text.Split('\n').Any(line => line.TrimStart().StartsWith("-parallel ", StringComparison.Ordinal) || line.Trim() == "-parallel");

            if (outcome.ExitCode != 0 || !takesParallel)
                refusing.Add($"{utility} (exit {outcome.ExitCode})");
        }

        Assert.That(refusing, Is.Empty, "utilities a parallel step may name, which the kit does not run with -parallel");
    }

    #endregion
}
