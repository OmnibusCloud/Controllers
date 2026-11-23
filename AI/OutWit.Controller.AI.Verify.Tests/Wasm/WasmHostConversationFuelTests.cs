using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// What a conversation costs, and that it is one run wherever it is held. The caller is asked
/// inside the guest's own read, so the same conversation is the same sequence of steps: the same
/// output and the same fuel on every run, in both trap modes and on an engine with exceptions on.
/// When the run's calls cost fuel, the reads of a conversation are paid for like the host's other
/// calls: the fixed price a call and a unit a byte handed over, and nothing besides.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostConversationFuelTests
{
    #region Constants

    private const int CAPACITY = 256;

    /// <summary>
    /// The fuel of the three-line conversation without the price of calls: the guest's own
    /// instructions, the same number on every platform and in every trap mode.
    /// </summary>
    private const long GUEST_FUEL = 108;

    private static readonly string[] LINES = ["one\n", "two\n", "three\n"];

    #endregion

    #region Fields

    private WasmHost m_host = null!;
    private WasmHost m_explicit = null!;
    private WasmHost m_exceptions = null!;
    private string m_root = null!;
    private string m_capture = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_host = new WasmHost();
        m_explicit = new WasmHost(parallelCompilation: true, explicitTraps: true);
        m_exceptions = new WasmHost(parallelCompilation: true, explicitTraps: false, exceptions: true);
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmtalkfuel_{Guid.NewGuid():N}");
        m_capture = Path.Combine(m_root, "capture");
        Directory.CreateDirectory(m_root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_host?.Dispose();
        m_explicit?.Dispose();
        m_exceptions?.Dispose();
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Same Run Tests

    [TestCase(false)]
    [TestCase(true)]
    public void TheFuelOfAConversationIsTheSameOnEveryRunTest(bool charge)
    {
        var runs = Enumerable.Range(0, 5).Select(_ => Hold(m_host, charge)).ToList();

        TestContext.Out.WriteLine($"three lines, the price of calls {(charge ? "on" : "off")}: {runs[0].Outcome.FuelConsumed} units of fuel");
        Assert.Multiple(() =>
        {
            Assert.That(runs[0].Outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), runs[0].Outcome.Stderr);
            Assert.That(runs[0].Outcome.ExitCode, Is.EqualTo(LINES.Length));
            foreach (var run in runs)
                AssertSameRun(run, runs[0], "another run");
        });
    }

    [Test]
    public void TheGuestsOwnFuelOfAConversationIsPinnedTest()
    {
        // The number is the guest's instructions alone, counted from its first one: a platform
        // or a trap mode that changed it would change what two nodes report for one run.
        var run = Hold(m_host, charge: false);

        Assert.That(run.Outcome.FuelConsumed, Is.EqualTo(GUEST_FUEL));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AConversationIsTheSameRunInBothTrapModesTest(bool charge)
    {
        if (m_host.ExplicitTraps)
            Assert.Ignore("this test process enforces shadow stacks, so its traps are explicit checks already: the two trap modes were NOT compared here");

        Assert.That(m_explicit.ExplicitTraps, Is.True);
        AssertSameRun(Hold(m_explicit, charge), Hold(m_host, charge), "explicit checks against hardware traps");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AConversationIsTheSameRunOnAnEngineWithExceptionsOnTest(bool charge)
    {
        Assert.That(m_exceptions.Exceptions, Is.True);
        AssertSameRun(Hold(m_exceptions, charge), Hold(m_host, charge), "exceptions on against exceptions off");
    }

    #endregion

    #region Price Tests

    [Test]
    public void WithThePriceOfCallsAConversationCostsItsCallsAndItsBytesAndNothingElseTest()
    {
        // Three lines in (14 bytes over three reads, and the read that finds the end), three
        // answers out (each line behind a prefix of two bytes), and the exit: eight calls, 34 bytes.
        const int CALLS = 4 + 3 + 1;
        const int BYTES = 14 + 20;
        var census = new WasmHostCallCensus();

        var free = Hold(m_host, charge: false);
        var charged = Hold(m_host, charge: true, census);

        TestContext.Out.WriteLine($"{free.Outcome.FuelConsumed} units without the price, {charged.Outcome.FuelConsumed} with it");
        Assert.Multiple(() =>
        {
            Assert.That(charged.Outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), charged.Outcome.Stderr);
            Assert.That(charged.Outcome.Stdout, Is.EqualTo(free.Outcome.Stdout));
            Assert.That(charged.Seen, Is.EqualTo(free.Seen));
            Assert.That(census.Lines(), Is.EqualTo(new[] { "fd_read 4 14", "fd_write 3 20", "proc_exit 1 0" }));
            Assert.That(census.Calls, Is.EqualTo(CALLS));
            Assert.That(census.Charged, Is.EqualTo(BYTES));
            Assert.That(charged.Outcome.FuelConsumed - free.Outcome.FuelConsumed, Is.EqualTo(CALLS * WasmHostMeter.CALL_FUEL + BYTES),
                "the fixed price of every call and a unit a byte, exactly");
        });
    }

    [Test]
    public void ARunOutOfFuelAtAReadOfTheConversationIsOutOfFuelTest()
    {
        // Fuel for the guest's instructions up to its first read, and not for the read.
        var conversation = WasmHostConversationScripted.Pieces(LINES);

        var outcome = WasmHostConversationRig.Run(m_host, m_capture, WasmHostConversationModules.Echo(CAPACITY), conversation, charge: true, fuel: WasmHostMeter.CALL_FUEL / 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(outcome.FuelConsumed, Is.EqualTo(WasmHostMeter.CALL_FUEL / 2));
            Assert.That(conversation.Seen, Is.Empty, "a read that is not paid for asks nobody");
        });
    }

    #endregion

    #region Tools

    /// <summary>Holds the three-line conversation with the echo guest on a host.</summary>
    private Talk Hold(WasmHost host, bool charge, WasmHostCallCensus? census = null)
    {
        var conversation = WasmHostConversationScripted.Pieces(LINES);
        var outcome = WasmHostConversationRig.Run(host, m_capture, WasmHostConversationModules.Echo(CAPACITY), conversation, charge, census: census);
        return new Talk(outcome, conversation.Seen);
    }

    private static void AssertSameRun(Talk run, Talk other, string what)
    {
        Assert.That(run.Outcome.Verdict, Is.EqualTo(other.Outcome.Verdict), $"{what}: the verdict");
        Assert.That(run.Outcome.ExitCode, Is.EqualTo(other.Outcome.ExitCode), $"{what}: the exit code");
        Assert.That(run.Outcome.Stdout, Is.EqualTo(other.Outcome.Stdout), $"{what}: stdout");
        Assert.That(run.Outcome.Stderr, Is.EqualTo(other.Outcome.Stderr), $"{what}: stderr");
        Assert.That(run.Outcome.FuelConsumed, Is.EqualTo(other.Outcome.FuelConsumed), $"{what}: the fuel");
        Assert.That(run.Outcome.PeakMemoryBytes, Is.EqualTo(other.Outcome.PeakMemoryBytes), $"{what}: the peak memory");
        Assert.That(run.Seen, Is.EqualTo(other.Seen), $"{what}: what the caller was shown, turn by turn");
    }

    #endregion

    #region Nested Types

    /// <summary>One conversation held: the run and what the caller was shown. A class: see <see cref="WasmHostConversationScripted"/> on what a test's types may hold.</summary>
    /// <param name="Outcome">The run's outcome.</param>
    /// <param name="Seen">What the caller was shown, turn by turn.</param>
    private sealed record Talk(WasmHostRunOutcome Outcome, IReadOnlyList<string> Seen);

    #endregion
}
