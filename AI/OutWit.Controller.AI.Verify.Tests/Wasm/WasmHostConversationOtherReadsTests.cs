using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// What a conversation does not touch (<see cref="IWasmHostConversation"/>): a read of any
/// descriptor but the guest's stdin goes the way it goes in a run without a conversation, a stdin
/// the guest closed is no longer the conversation's, and a run that has no conversation leaves
/// its reads to WASI as it always did.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostConversationOtherReadsTests
{
    #region Constants

    private const int CAPACITY = 256;

    #endregion

    #region Fields

    private WasmHost m_host = null!;
    private string m_root = null!;
    private string m_capture = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_host = new WasmHost();
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmtalkother_{Guid.NewGuid():N}");
        m_capture = Path.Combine(m_root, "capture");
        Directory.CreateDirectory(m_root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_host?.Dispose();
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [TestCase(false)]
    [TestCase(true)]
    public void ADescriptorThatIsNotThereIsReadAsWithoutAConversationTest(bool charge)
    {
        var module = WasmHostConversationModules.ReadOnce(99, WasmHostConversationModules.BUFFER, 16, 0, 1, 8);
        var conversation = WasmHostConversationScripted.Pieces("input\n");

        var with = Run(module, conversation, charge);
        var without = Run(module, null, charge);

        Assert.Multiple(() =>
        {
            Assert.That(without.ExitCode, Is.EqualTo(WasmHostErrno.BADF), "WASI's answer");
            Assert.That(with.Verdict, Is.EqualTo(without.Verdict));
            Assert.That(with.ExitCode, Is.EqualTo(without.ExitCode));
            Assert.That(with.FuelConsumed, Is.EqualTo(without.FuelConsumed));
            Assert.That(conversation.Seen, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AFileIsReadAsWithoutAConversationTest(bool charge)
    {
        var root = Path.Combine(m_root, $"files-{charge}");
        var input = Path.Combine(root, "in");
        var conversation = WasmHostConversationScripted.Pieces("input\n");
        Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, WasmHostModules.INPUT_NAME), "page one\fpage two\n");

        var with = Copy(Path.Combine(root, "with"), input, conversation, charge);
        var without = Copy(Path.Combine(root, "without"), input, null, charge);

        Assert.Multiple(() =>
        {
            Assert.That(with.Outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), with.Outcome.Stderr);
            Assert.That(with.Outcome.ExitCode, Is.EqualTo(0));
            Assert.That(with.Copied, Is.EqualTo("page one\fpage two\n"), "the file's bytes, not the conversation's");
            Assert.That(with.Copied, Is.EqualTo(without.Copied));
            Assert.That(with.Outcome.FuelConsumed, Is.EqualTo(without.Outcome.FuelConsumed));
            Assert.That(conversation.Seen, Is.Empty);
        });
    }

    [Test]
    public void AStdinTheGuestClosedIsNoLongerTheConversationTest()
    {
        // The number of a closed descriptor is the guest's to open a file under: what is read
        // from it then is that file, as in a run without a conversation.
        var conversation = WasmHostConversationScripted.Pieces("input\n");

        var with = Run(WasmHostConversationModules.CloseThenRead(), conversation);
        var without = Run(WasmHostConversationModules.CloseThenRead(), null);

        TestContext.Out.WriteLine($"close then read: exit {without.ExitCode} without a conversation, {with.ExitCode} with one");
        Assert.Multiple(() =>
        {
            Assert.That(without.Verdict, Is.EqualTo(WasmHostVerdict.Exited), without.Stderr);
            Assert.That(without.ExitCode, Is.EqualTo(WasmHostErrno.BADF), "the close went through, and the read finds no descriptor");
            Assert.That(with.Verdict, Is.EqualTo(without.Verdict));
            Assert.That(with.ExitCode, Is.EqualTo(without.ExitCode));
            Assert.That(conversation.Seen, Is.Empty);
        });
    }

    [Test]
    public void ARunWithoutAConversationLeavesItsReadsToWasiTest()
    {
        // The host counts the calls it answers. Without a conversation (and without the price of
        // calls) a read is WASI's own, as it always was: the host never sees it.
        var plain = new WasmHostCallCensus();
        var talking = new WasmHostCallCensus();

        var without = Run(WasmHostConversationModules.Echo(CAPACITY), null, stdin: "one\n", census: plain);
        var with = Run(WasmHostConversationModules.Echo(CAPACITY), WasmHostConversationScripted.Pieces("one\n"), census: talking);

        Assert.Multiple(() =>
        {
            Assert.That(without.Stdout, Is.EqualTo("> one\n"));
            Assert.That(with.Stdout, Is.EqualTo(without.Stdout));
            Assert.That(with.ExitCode, Is.EqualTo(without.ExitCode));
            Assert.That(plain.Lines(), Is.EqualTo(new[] { "fd_write 1 6", "proc_exit 1 0" }), "no read among the host's calls");
            Assert.That(talking.Lines(), Is.EqualTo(new[] { "fd_read 2 4", "fd_write 1 6", "proc_exit 1 0" }), "the two reads of the conversation, and the four bytes it gave");
        });
    }

    #endregion

    #region Tools

    private WasmHostRunOutcome Run(byte[] bytes, IWasmHostConversation? conversation, bool charge = false, string? stdin = null, WasmHostCallCensus? census = null)
    {
        return WasmHostConversationRig.Run(m_host, m_capture, bytes, conversation, charge, stdin: stdin, census: census);
    }

    /// <summary>Runs the copy program (it reads a file of its first folder into its second) and reads what it wrote.</summary>
    private (WasmHostRunOutcome Outcome, string Copied) Copy(string output, string input, IWasmHostConversation? conversation, bool charge)
    {
        Directory.CreateDirectory(output);
        var outcome = WasmHostConversationRig.Run(m_host, m_capture, WasmHostModules.Copy(), conversation, charge,
            preopens: [new WasmHostPreopen(input, "/in"), new WasmHostPreopen(output, "/out", Writable: true)]);

        var copy = Path.Combine(output, WasmHostModules.OUTPUT_NAME);
        return (outcome, File.Exists(copy) ? File.ReadAllText(copy) : "");
    }

    #endregion
}
