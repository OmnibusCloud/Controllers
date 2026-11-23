using System.Text.RegularExpressions;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Profiles;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Solver;

/// <summary>
/// The profile: the one pinned solver of this version, held to the pins of the toolchain that
/// builds its module, and the rules that turn a run's options into the envelope of a session:
/// the fuel of a hard cap, the host's cap of the memory above the solver's own ceiling, the
/// backstop, the defaults.
/// </summary>
[TestFixture]
public sealed class SmtProfileTests
{
    #region Constants

    private const string PINS = "AI/OutWit.Controller.AI.Smt.Assets/toolchain/pins.env";
    private const string SOLUTION_FILE = "OutWit.slnx";

    private const long MEBIBYTE = 1L << 20;
    private const long GIBIBYTE = 1L << 30;

    #endregion

    #region Pin Tests

    [Test]
    public void TheModuleIsTheOneTheToolchainPinsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Pin("Z3_VERSION"), Is.EqualTo(SmtProfile.SOLVER_VERSION));
            Assert.That(Pin("Z3_MODULE_SHA256"), Is.EqualTo(SmtProfile.MODULE_SHA256), "the module the controller compiles is the one the toolchain builds");
            Assert.That(long.Parse(Pin("Z3_MODULE_SIZE")), Is.EqualTo(SmtProfile.MODULE_SIZE));
            Assert.That($"z3-{Pin("Z3_VERSION")}.wasm", Is.EqualTo(SmtProfile.MODULE_FILE), "the file's name in the toolchain's archive");
            Assert.That($"z3-{Pin("Z3_VERSION")}", Is.EqualTo(SmtProfile.ID));
            Assert.That(SmtSolverModule.RELATIVE_PATH, Is.EqualTo($"{SmtProfile.MODULE_FOLDER}/{SmtProfile.MODULE_FILE}"), "where the tests look for the module");
        });
    }

    [Test]
    public void TheProfileIsNamedByItsIdOrByNoNameTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtProfile.Is(""), Is.True);
            Assert.That(SmtProfile.Is(null), Is.True);
            Assert.That(SmtProfile.Is("z3-5.4.1"), Is.True);
            Assert.That(SmtProfile.Is("Z3-5.4.1"), Is.False);
            Assert.That(SmtProfile.Is("z3-5.4.0"), Is.False);
            Assert.That(SmtProfile.Is(" "), Is.False);
        });
    }

    #endregion

    #region Rule Tests

    [TestCase(1, 5_000_000_000)]
    [TestCase(60, 300_000_000_000)]
    [TestCase(100, 500_000_000_000)]
    [TestCase(600, 3_000_000_000_000)]
    public void AHardCapInSecondsIsFuelAtTheReferenceRateTest(int seconds, long fuel)
    {
        Assert.That(SmtProfile.Fuel(seconds), Is.EqualTo(fuel));
    }

    [TestCase(1_024, 1_280 * MEBIBYTE, TestName = "{m}(the default: a quarter above)")]
    [TestCase(256, 320 * MEBIBYTE)]
    [TestCase(64, 80 * MEBIBYTE)]
    [TestCase(63, 79 * MEBIBYTE, TestName = "{m}(below 64 MB a quarter is less than the solver's fixed part: sixteen above)")]
    [TestCase(8, 24 * MEBIBYTE)]
    [TestCase(1, 17 * MEBIBYTE, TestName = "{m}(the least option: the module still starts, in 17 MiB)")]
    [TestCase(3_276, 4_095 * MEBIBYTE)]
    [TestCase(3_277, 4 * GIBIBYTE, TestName = "{m}(never more than a wasm32 program can address)")]
    [TestCase(4_096, 4 * GIBIBYTE)]
    public void TheHostsCapOfTheMemoryStandsAboveTheSolversOwnCeilingTest(int megabytes, long cap)
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtProfile.HostMemoryBytes(megabytes), Is.EqualTo(cap));
            if (megabytes < 3_277)
                Assert.That(SmtProfile.HostMemoryBytes(megabytes), Is.GreaterThan(megabytes * MEBIBYTE), "the solver meets its own ceiling first, and ends cleanly");
        });
    }

    [Test]
    public void TheBackstopIsGenerousAndGrowsWithTheFuelTest()
    {
        var minute = SmtProfile.BackstopMs(SmtProfile.Fuel(60));
        var most = SmtProfile.BackstopMs(SmtProfile.Fuel(SmtOptionsRules.MAX_HARD_CAP_SECONDS));

        Assert.Multiple(() =>
        {
            Assert.That(minute, Is.EqualTo(660_000), "a minute, and ten times the minute the fuel takes at the reference rate");
            Assert.That(most, Is.EqualTo(6_060_000));
            Assert.That(SmtProfile.BackstopMs(0), Is.EqualTo(60_000));
            Assert.That(SmtProfile.BackstopMs(long.MaxValue), Is.EqualTo(int.MaxValue), "it never wraps");
        });
    }

    #endregion

    #region Envelope Tests

    [Test]
    public void OptionsThatAreNotSetGiveTheDefaultsTest()
    {
        var envelope = SmtProfile.Envelope(new SmtOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(envelope.ResourceLimit, Is.EqualTo(150_000_000), "the profile's resource limit");
            Assert.That(envelope.Fuel, Is.EqualTo(300_000_000_000), "sixty seconds of the reference rate");
            Assert.That(envelope.MemoryMegabytes, Is.EqualTo(1_024));
            Assert.That(envelope.HostMemoryBytes, Is.EqualTo(1_280 * MEBIBYTE));
            Assert.That(envelope.OutputLimitBytes, Is.EqualTo(16 * MEBIBYTE));
            Assert.That(envelope.BackstopMs, Is.EqualTo(660_000));
            Assert.That(envelope.Seed, Is.Zero);
            Assert.That(envelope.MaxModels, Is.EqualTo(2));
        });
    }

    [Test]
    public void OptionsThatAreSetAreTakenTest()
    {
        var envelope = SmtProfile.Envelope(new SmtOptionsData { ResourceLimit = 2_000_000_000, HardCapSeconds = 100, MemoryMegabytes = 512, Seed = 9, MaxModels = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(envelope.ResourceLimit, Is.EqualTo(2_000_000_000));
            Assert.That(envelope.Fuel, Is.EqualTo(500_000_000_000));
            Assert.That(envelope.MemoryMegabytes, Is.EqualTo(512));
            Assert.That(envelope.HostMemoryBytes, Is.EqualTo(640 * MEBIBYTE));
            Assert.That(envelope.Seed, Is.EqualTo(9));
            Assert.That(envelope.MaxModels, Is.EqualTo(50));
        });
    }

    [TestCase(4_294_967_296)]
    [TestCase(0)]
    [TestCase(-1)]
    public void NoSessionIsMadeWithAResourceLimitTheSolverDoesNotTakeTest(long limit)
    {
        // Above 32 bits the solver reports an error and then runs the check with no limit at
        // all. The options refuse such a number; an envelope made another way is refused here.
        var envelope = SmtProfile.Envelope(new SmtOptionsData()) with { ResourceLimit = limit };
        var plan = SmtSolvePlan.Prepare(SmtSolverProblems.Check("limit", "(declare-const x Int)"), envelope, out _);

        Assert.That(plan, Is.Not.Null);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SmtSession(plan!, envelope, CancellationToken.None));
    }

    [Test]
    public void TheSolverIsStartedOnItsInputWithItsOwnMemoryCeilingTest()
    {
        var envelope = SmtProfile.Envelope(new SmtOptionsData { MemoryMegabytes = 512 });

        Assert.That(SmtProfile.Arguments(envelope), Is.EqualTo(new[] { "z3", "-in", "-smt2", "-memory:512" }));
    }

    [Test]
    public void TheOptionsTheControllerSetsAreNoSuccessLinesModelsAndTheSeedsTest()
    {
        Assert.That(SmtScripts.Text(SmtProfile.Options(31)), Is.EqualTo(
            "(set-option :print-success false)\n(set-option :produce-models true)\n(set-option :smt.random_seed 31)\n(set-option :sat.random_seed 31)\n"));
    }

    #endregion

    #region Tools

    /// <summary>A value of the solver toolchain's pins (<c>toolchain/pins.env</c>).</summary>
    private static string Pin(string name)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            directory = directory.Parent;

        Assert.That(directory, Is.Not.Null, $"{SOLUTION_FILE} above the test folder");

        var pins = File.ReadAllText(Path.Combine(directory!.FullName, PINS.Replace('/', Path.DirectorySeparatorChar)));
        var match = Regex.Match(pins, $@"^{Regex.Escape(name)}=(?<value>\S+)\s*$", RegexOptions.Multiline);
        Assert.That(match.Success, Is.True, $"pins.env names {name}");
        return match.Groups["value"].Value;
    }

    #endregion
}
