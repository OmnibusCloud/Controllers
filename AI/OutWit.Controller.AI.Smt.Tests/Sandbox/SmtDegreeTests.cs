using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Docs;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Sandbox;

/// <summary>
/// How many sessions a package runs at once: the cores, a cap of 16, and what three quarters of
/// the available memory holds at twice the host's cap of the solver's linear memory and a margin a
/// session; and the least memory of a machine the planner sends a package to. The machines are
/// numbers given to the rule, so every machine is any machine here.
/// </summary>
[TestFixture]
public sealed class SmtDegreeTests
{
    #region Constants

    private const long GIGABYTE = 1L << 30;
    private const long MEGABYTE = 1L << 20;

    #endregion

    #region Session Tests

    [Test]
    public void ASessionTakesTwiceItsHostCapAndTheMarginTest()
    {
        Assert.Multiple(() =>
        {
            // The default: a solver's ceiling of 1,024 MB, a host cap a quarter above it.
            Assert.That(SmtNodeRig.HostMemoryBytes(1_024), Is.EqualTo(1_280 * MEGABYTE));
            Assert.That(SmtNodeRig.SessionBytes(new SmtOptionsData()), Is.EqualTo(2 * 1_280 * MEGABYTE + SmtNodeRig.SessionMarginBytes));

            // The ceiling of the options: the host cap is what a wasm32 guest can address.
            Assert.That(SmtNodeRig.SessionBytes(new SmtOptionsData { MemoryMegabytes = 4_096 }), Is.EqualTo(2 * 4 * GIGABYTE + SmtNodeRig.SessionMarginBytes));

            // A small ceiling: the host cap's floor of 16 MiB above it.
            Assert.That(SmtNodeRig.SessionBytes(new SmtOptionsData { MemoryMegabytes = 4 }), Is.EqualTo(2 * 20 * MEGABYTE + SmtNodeRig.SessionMarginBytes));
        });
    }

    #endregion

    #region Degree Tests

    [TestCase(4, 4, 1, "memory")]
    [TestCase(4, 16, 4, "cores")]
    [TestCase(4, 64, 4, "cores")]
    [TestCase(16, 4, 1, "memory")]
    [TestCase(16, 16, 4, "memory")]
    [TestCase(16, 64, 16, "cores")]
    [TestCase(64, 4, 1, "memory")]
    [TestCase(64, 16, 4, "memory")]
    [TestCase(64, 64, 16, "cap")]
    public void TheDegreeOfAMachineUnderTheDefaultOptionsTest(int cores, int gigabytes, int degree, string reason)
    {
        // A session under the default options takes 2.5 GiB and the margin: three quarters of
        // 4 GiB hold one, of 16 GiB four, of 64 GiB seventeen.
        var session = SmtNodeRig.SessionBytes(new SmtOptionsData());

        Assert.That(SmtNodeRig.Degree(cores, gigabytes * GIGABYTE, session), Is.EqualTo((degree, reason)));
    }

    [TestCase(4, 4, 0, "no_memory")]
    [TestCase(16, 16, 1, "memory")]
    [TestCase(64, 64, 5, "memory")]
    public void TheDegreeOfAMachineUnderTheLargestMemoryTest(int cores, int gigabytes, int degree, string reason)
    {
        // A session of 4,096 MB takes two caps of 4 GiB and the margin.
        var session = SmtNodeRig.SessionBytes(new SmtOptionsData { MemoryMegabytes = 4_096 });

        Assert.That(SmtNodeRig.Degree(cores, gigabytes * GIGABYTE, session), Is.EqualTo((degree, reason)));
    }

    [Test]
    public void AMachineThatCannotSayItsMemoryRunsByItsCoresTest()
    {
        var session = SmtNodeRig.SessionBytes(new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Degree(8, 0, session), Is.EqualTo((8, "cores")));
            Assert.That(SmtNodeRig.Degree(40, 0, session), Is.EqualTo((SmtNodeRig.MaxDegree, "cap")));
            Assert.That(SmtNodeRig.Degree(0, 0, session), Is.EqualTo((1, "cores")), "a machine counts one core at the least");
        });
    }

    [Test]
    public void TheRuleIsTheThreeNumbersAndTheHeadroomTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.MaxDegree, Is.EqualTo(16));
            Assert.That(SmtNodeRig.RamHeadroom, Is.EqualTo(0.75));

            // Just enough for one session, and just not.
            var session = 3 * GIGABYTE;
            Assert.That(SmtNodeRig.Degree(4, 4 * GIGABYTE, session), Is.EqualTo((1, "memory")));
            Assert.That(SmtNodeRig.Degree(4, 4 * GIGABYTE - 1, session), Is.EqualTo((0, "no_memory")));
        });
    }

    #endregion

    #region Requirement Tests

    [Test]
    public void TheActivityThatSolvesAPackageAsksForMachinesOfEightGigabytesTest()
    {
        // The server's planner reads the requirement off the activity; the capabilities document
        // states the same number.
        var capabilities = JsonDocument.Parse(SmtDiagnosticsRig.Capabilities(new SmtRuntimeDiagnosticsData { ModulePresent = true })).RootElement;
        var session = SmtNodeRig.SessionBytes(new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.SolvePackageMinRamMb(), Is.EqualTo(new[] { SmtNodeRig.MinRamMb }), "one RequiresResources, its MinRamMb the constant");
            Assert.That(SmtNodeRig.MinRamMb, Is.EqualTo(8_192), "machines of 8 GB at least");
            Assert.That(capabilities.GetProperty("node").GetProperty("min_ram_mb").GetInt64(), Is.EqualTo(SmtNodeRig.MinRamMb), "the capabilities document carries it");
            Assert.That(SmtSchemaCheck.IsValid(SmtRunChecks.Schema("smt-capabilities.schema.json"), capabilities), Is.True, "and holds to its schema");

            // Why: one session at the defaults needs less than half of such a machine available,
            // and a machine with 2 GiB available (a 6 GB machine with its client running) holds none.
            Assert.That(session / SmtNodeRig.RamHeadroom, Is.LessThan(SmtNodeRig.MinRamMb * MEGABYTE / 2));
            Assert.That(SmtNodeRig.Degree(4, 2 * GIGABYTE, session), Is.EqualTo((0, "no_memory")));
        });
    }

    #endregion

    #region Cancel Tests

    [Test]
    public void ACancelWaitsAtWorstForTheBackstopOfARunningSessionTest()
    {
        // A running check ends at its next read, by its resource limit, by its fuel, or by the
        // backstop: a minute and ten times the hard cap.
        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.CancelWorstCaseMs(new SmtOptionsData()), Is.EqualTo(660_000), "the default hard cap of 60 s");
            Assert.That(SmtNodeRig.CancelWorstCaseMs(new SmtOptionsData { HardCapSeconds = 600 }), Is.EqualTo(6_060_000), "the largest hard cap");
            Assert.That(SmtNodeRig.CancelWorstCaseMs(new SmtOptionsData { HardCapSeconds = 1 }), Is.EqualTo(70_000));
        });
    }

    #endregion
}
