using OutWit.Controller.AI.Wasm;
using Wasmtime;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// A conversation over a guest's stdin and stdout (<see cref="IWasmHostConversation"/>): the
/// caller answers the guest's reads one by one, having seen what the guest wrote so far. The
/// turns and the end of input, the read's own rules (one buffer, no more than it holds, a bad
/// pointer refused before anybody is asked), the bounds that hold with a conversation as without
/// (the fuel, the cap of stdout), and the caller's failure as a fault of the host's side. What a
/// conversation does not touch is <see cref="WasmHostConversationOtherReadsTests"/>.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostConversationTests
{
    #region Constants

    private const int CAPACITY = 256;
    private const string ECHO = "> aaaa\n";

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
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmtalk_{Guid.NewGuid():N}");
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

    #region Turn Tests

    [Test]
    public void TheCallerSeesEachAnswerBeforeItGivesTheNextLineTest()
    {
        var conversation = WasmHostConversationScripted.Pieces("one\n", "two\n", "three\n");

        var outcome = Run(WasmHostConversationModules.Echo(CAPACITY), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(3), "the guest counts the lines it read and exits with the count once its input ends");
            Assert.That(conversation.Seen, Is.EqualTo(new[] { "", "> one\n", "> two\n", "> three\n" }), "asked at the first read with nothing written yet, then once an answer, then for the end");
            Assert.That(outcome.Stdout, Is.EqualTo("> one\n> two\n> three\n"), "the outcome holds the whole stdout, as without a conversation");
            Assert.That(outcome.Stderr, Is.Empty);
        });
    }

    [Test]
    public void TheCallerDecidesTheNextLineFromTheAnswerTest()
    {
        // A countdown the caller holds no list of: every line is one less than the guest's last answer.
        var conversation = new WasmHostConversationScripted((turn, answer) =>
        {
            if (turn == 0)
                return "3\n";

            var number = int.Parse(answer.AsSpan(WasmHostConversationModules.PREFIX.Length).Trim());
            return number == 0 ? null : $"{number - 1}\n";
        });

        var outcome = Run(WasmHostConversationModules.Echo(CAPACITY), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(4));
            Assert.That(outcome.Stdout, Is.EqualTo("> 3\n> 2\n> 1\n> 0\n"));
        });
    }

    [Test]
    public void ALineLongerThanTheGuestsBufferComesOverSeveralReadsTest()
    {
        const string LINE = "0123456789abcdefghijklmnopqrstuvwxyz+-*\n";
        var conversation = WasmHostConversationScripted.Pieces(LINE);

        var outcome = Run(WasmHostConversationModules.Echo(16), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(LINE, Has.Length.EqualTo(40));
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(3), "sixteen bytes, sixteen and eight: a read gives no more than its buffer holds");
            Assert.That(outcome.Stdout, Is.EqualTo("> 0123456789abcdef> ghijklmnopqrstuv> wxyz+-*\n"));
            Assert.That(conversation.Seen, Has.Count.EqualTo(2), "one turn for the line, however many reads took it in, and one for the end");
            Assert.That(conversation.Seen[1], Is.EqualTo(outcome.Stdout));
        });
    }

    [Test]
    public void InputOfZeroBytesAtOnceIsTheEndOfInputTest()
    {
        var conversation = WasmHostConversationScripted.Pieces();

        var outcome = Run(WasmHostConversationModules.Echo(CAPACITY), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(0));
            Assert.That(outcome.Stdout, Is.Empty);
            Assert.That(conversation.Seen, Is.EqualTo(new[] { "" }));
        });
    }

    [Test]
    public void AfterTheEndOfInputEveryReadGivesNothingAndNobodyIsAskedTest()
    {
        var conversation = WasmHostConversationScripted.Pieces("ab");

        var outcome = Run(WasmHostConversationModules.ReadTimes(6), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(2), "two bytes from the first read, none from the five after it");
            Assert.That(conversation.Seen, Has.Count.EqualTo(2), "asked for the input and for what follows it, and not after the end");
        });
    }

    [Test]
    public void AReadWithSeveralBuffersFillsTheFirstThatHasRoomTest()
    {
        var conversation = WasmHostConversationScripted.Pieces("abcdefgh");

        var outcome = Run(WasmHostConversationModules.Vector(), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(44), "four bytes a read: the buffer of four after the empty one, and never the third");
            Assert.That(outcome.Stdout, Is.EqualTo("abcd........efgh........"));
            Assert.That(conversation.Seen, Has.Count.EqualTo(1), "the second read took what was still pending");
        });
    }

    #endregion

    #region Pointer Tests

    [TestCase("the list crosses the end of the memory", WasmHostConversationModules.BUFFER, 16, WasmHostConversationModules.MEMORY_BYTES - 4, 1, 8, WasmHostErrno.FAULT)]
    [TestCase("the list is far outside", WasmHostConversationModules.BUFFER, 16, -8, 1, 8, WasmHostErrno.FAULT)]
    [TestCase("the buffer crosses the end of the memory", WasmHostConversationModules.MEMORY_BYTES - 6, 16, 0, 1, 8, WasmHostErrno.FAULT)]
    [TestCase("the buffer is far outside", -16, 16, 0, 1, 8, WasmHostErrno.FAULT)]
    [TestCase("the place of the count crosses the end of the memory", WasmHostConversationModules.BUFFER, 16, 0, 1, WasmHostConversationModules.MEMORY_BYTES - 2, WasmHostErrno.FAULT)]
    [TestCase("the place of the count is far outside", WasmHostConversationModules.BUFFER, 16, 0, 1, -4, WasmHostErrno.FAULT)]
    [TestCase("more buffers than a read may name", WasmHostConversationModules.BUFFER, 16, 0, WasmHostMemory.MAX_BUFFERS + 1, 8, WasmHostErrno.INVAL)]
    [TestCase("a negative number of buffers", WasmHostConversationModules.BUFFER, 16, 0, -1, 8, WasmHostErrno.INVAL)]
    public void ABadPointerIsRefusedAndNobodyIsAskedTest(string what, int pointer, int length, int buffers, int count, int read, int errno)
    {
        Assert.Multiple(() =>
        {
            foreach (var charge in new[] { false, true })
            {
                var conversation = WasmHostConversationScripted.Pieces("input\n");

                var outcome = Run(WasmHostConversationModules.ReadOnce(WasmHostConversationInput.DESCRIPTOR, pointer, length, buffers, count, read), conversation, charge);

                Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), $"{what}: an errno, never an error of the engine; {outcome.Stderr}");
                Assert.That(outcome.ExitCode, Is.EqualTo(errno), what);
                Assert.That(conversation.Seen, Is.Empty, $"{what}: no input is asked for a read that cannot take it");
            }
        });
    }

    [TestCase("no buffer at all", 16, 0)]
    [TestCase("a buffer of no length", 0, 1)]
    public void AReadWithoutRoomTakesNothingAndAsksNobodyTest(string what, int length, int count)
    {
        var conversation = WasmHostConversationScripted.Pieces("input\n");

        var outcome = Run(WasmHostConversationModules.ReadOnce(WasmHostConversationInput.DESCRIPTOR, WasmHostConversationModules.BUFFER, length, 0, count, 8), conversation);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(WasmHostErrno.SUCCESS), what);
            Assert.That(conversation.Seen, Is.Empty, what);
        });
    }

    #endregion

    #region Bound Tests

    [TestCase(false)]
    [TestCase(true)]
    public void AConversationWithoutEndIsEndedByFuelTest(bool charge)
    {
        const long BUDGET = 300_000;
        var conversation = new WasmHostConversationScripted((_, _) => "again\n");

        var outcome = Run(WasmHostConversationModules.Echo(CAPACITY), conversation, charge, fuel: BUDGET, stdoutLimitBytes: 1024);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(outcome.Trap, Is.EqualTo(TrapCode.OutOfFuel));
            Assert.That(outcome.FuelConsumed, Is.EqualTo(BUDGET));
            Assert.That(outcome.Fault, Is.Null, "a budget stop is not named");
            Assert.That(conversation.Seen, Has.Count.GreaterThan(10), "the conversation went on until the fuel was gone");
        });
    }

    [Test]
    public void TheConversationSeesOnlyWhatTheCapOfStdoutKeptTest()
    {
        // Three answers of seven bytes under a cap of ten: the first whole, three bytes of the second, nothing of the third.
        var conversation = WasmHostConversationScripted.Pieces("aaaa\n", "aaaa\n", "aaaa\n");

        var outcome = Run(WasmHostConversationModules.Echo(CAPACITY), conversation, stdoutLimitBytes: 10);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), "without the stop the run goes on");
            Assert.That(outcome.ExitCode, Is.EqualTo(3));
            Assert.That(conversation.Seen, Is.EqualTo(new[] { "", ECHO, ECHO[..3], "" }));
            Assert.That(outcome.Stdout, Is.EqualTo(ECHO + ECHO[..3]));
            Assert.That(outcome.StdoutTruncated, Is.True);
            Assert.That(outcome.StdoutBytes, Is.EqualTo(10));
        });
    }

    [Test]
    public void TheWriteThatPassesTheCapEndsAConversationWhenAskedToTest()
    {
        var conversation = WasmHostConversationScripted.Pieces("aaaa\n", "aaaa\n", "aaaa\n");

        var outcome = Run(WasmHostConversationModules.Echo(CAPACITY), conversation, stdoutLimitBytes: 10, stopAtStdoutLimit: true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutputLimit));
            Assert.That(conversation.Seen, Is.EqualTo(new[] { "", ECHO }), "the second answer ended the run: nobody was asked after it");
            Assert.That(outcome.Stdout, Is.EqualTo(ECHO + ECHO[..3]));
            Assert.That(outcome.StdoutTruncated, Is.True);
        });
    }

    #endregion

    #region Fault Tests

    [Test]
    public void TheCallersExceptionIsAFaultOfTheHostNotAVerdictTest()
    {
        var failure = new InvalidOperationException("the caller failed");
        var capture = Path.Combine(m_root, "capture-fault");
        var conversation = new WasmHostConversationScripted((turn, _) => turn == 0 ? "one\n" : throw failure);

        var exception = Assert.Throws<IOException>(() => WasmHostConversationRig.Run(m_host, capture, WasmHostConversationModules.Echo(CAPACITY), conversation));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(WasmHost.HOST_FAULT));
            Assert.That(exception.InnerException, Is.SameAs(failure));
            Assert.That(Directory.GetFileSystemEntries(capture), Is.Empty, "the run that failed left nothing behind");
        });
    }

    [Test]
    public void ACallersExceptionOfTheEnginesKindIsAFaultOfTheHostAsWellTest()
    {
        // The host reads a run's end by the engine's exception type; a caller's exception of that
        // type would end the run as an engine error, a verdict.
        var failure = new WasmtimeException("the caller failed in the engine's words");
        var conversation = new WasmHostConversationScripted((_, _) => throw failure);

        var exception = Assert.Throws<IOException>(() => Run(WasmHostConversationModules.Echo(CAPACITY), conversation));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(WasmHost.HOST_FAULT));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException!.Message, Is.EqualTo(WasmHostConversationInput.CALLER_FAULT));
            Assert.That(exception.InnerException.InnerException, Is.SameAs(failure));
        });
    }

    [Test]
    public void AConversationTogetherWithAStdinTextIsRefusedBeforeAnythingStartsTest()
    {
        var capture = Path.Combine(m_root, "capture-refused-stdin");
        var conversation = WasmHostConversationScripted.Pieces("one\n");

        var exception = Assert.Throws<ArgumentException>(() =>
            WasmHostConversationRig.Run(m_host, capture, WasmHostConversationModules.Echo(CAPACITY), conversation, stdin: "two\n"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("a conversation or a stdin text, not both"));
            Assert.That(conversation.Seen, Is.Empty);
            Assert.That(Directory.Exists(capture), Is.False, "nothing of the run was made");
        });
    }

    [Test]
    public void AConversationTogetherWithAFileForStdoutIsRefusedBeforeAnythingStartsTest()
    {
        var capture = Path.Combine(m_root, "capture-refused-file");
        var file = Path.Combine(m_root, "stdout.bin");
        var conversation = WasmHostConversationScripted.Pieces("one\n");

        var exception = Assert.Throws<ArgumentException>(() =>
            WasmHostConversationRig.Run(m_host, capture, WasmHostConversationModules.Echo(CAPACITY), conversation, stdoutFile: file));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("it cannot go to a file"));
            Assert.That(conversation.Seen, Is.Empty);
            Assert.That(Directory.Exists(capture), Is.False, "nothing of the run was made");
            Assert.That(File.Exists(file), Is.False, "the file was not made either");
        });
    }

    #endregion

    #region Tools

    private WasmHostRunOutcome Run(byte[] bytes, IWasmHostConversation? conversation, bool charge = false, long fuel = WasmHostConversationRig.FUEL,
        int stdoutLimitBytes = 0, bool stopAtStdoutLimit = false)
    {
        return WasmHostConversationRig.Run(m_host, m_capture, bytes, conversation, charge, fuel, stdoutLimitBytes, stopAtStdoutLimit);
    }

    #endregion
}
