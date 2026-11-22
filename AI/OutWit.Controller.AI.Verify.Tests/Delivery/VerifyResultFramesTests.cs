using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using MemoryPack;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Delivery;

/// <summary>
/// The walk of a result package's frames (<see cref="VerifyResultFrames"/>): what a node wrote is
/// held to the frame's own bytes before a decoder sees it. Every frame an honest writer makes
/// passes; a length that leaves the frame, a count outside its bound, a text that is not the
/// text its header states and a byte too few or too many are refused with one fixed exception;
/// and a frame the walk passes is one the decoder reads. The frames here are written by hand,
/// by a writer of the test's own that is held to the serializer's bytes first. A frame the walk
/// refuses is never handed on: the tests of refusals call the walk alone.
/// </summary>
[TestFixture]
public sealed class VerifyResultFramesTests
{
    #region Constants

    private const int MUTATIONS = 20_000;
    private const int SEED = 20261119;

    #endregion

    #region Honest Frames Tests

    [Test]
    public void P11EveryFrameAWriterWritesIsWalkedToItsEndTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var result in HonestResults())
                Assert.That(() => VerifyResultFrames.CheckResult(MemoryPackSerializer.Serialize(result)), Throws.Nothing, result.Stdout.Length > 40 ? "a long one" : result.Stdout);

            foreach (var batch in HonestHeads())
                Assert.That(() => VerifyResultFrames.CheckBatch(MemoryPackSerializer.Serialize(batch)), Throws.Nothing, batch.Ticket);
        });
    }

    [Test]
    public void P11TheWalkCountsTheMembersTheModelHasTest()
    {
        // The walk knows the layout by heart. A member added to the model without the walk
        // fails here, and so does one the walk has and the model does not.
        Assert.Multiple(() =>
        {
            Assert.That(VerifyResultFrames.BATCH_MEMBERS, Is.EqualTo(Members(typeof(VerifyResultBatchData))));
            Assert.That(VerifyResultFrames.DIAGNOSTICS_MEMBERS, Is.EqualTo(Members(typeof(VerifyBatchDiagnosticsData))));
            Assert.That(VerifyResultFrames.RESULT_MEMBERS, Is.EqualTo(Members(typeof(VerifyResultData))));
            Assert.That(VerifyResultFrames.CASE_MEMBERS, Is.EqualTo(Members(typeof(VerifyCaseResultData))));

            Assert.That(MemoryPackSerializer.Serialize(new VerifyResultData())[0], Is.EqualTo(VerifyResultFrames.RESULT_MEMBERS), "an object opens with its member count");
            Assert.That(MemoryPackSerializer.Serialize(new VerifyResultBatchData())[0], Is.EqualTo(VerifyResultFrames.BATCH_MEMBERS));
            Assert.That(VerifyResultFrames.MAX_CASES, Is.EqualTo(1024), "a result has at most as many case records as a suite has cases");
        });
    }

    [Test]
    public void P11TheFramesWrittenByHandAreTheSerializersBytesTest()
    {
        // The writer the hostile frames below are made with, held to the real one.
        var result = new VerifyResultData
        {
            TaskIndex = 7, Verdict = VerifyVerdict.Fail, ExitCode = 3, Stdout = "héllo \U0001F600", StdoutTruncated = true, Stderr = "e", FuelConsumed = 5, PeakMemoryBytes = 6, WallMs = 7, Fault = "",
            CaseResults = [new VerifyCaseResultData { CaseIndex = 0, Passed = false, Verdict = VerifyVerdict.Fail, ExitCode = 3, ActualStdout = "x" }]
        };

        var byHand = new List<byte> { 12 };
        Int32(byHand, 7);
        Int32(byHand, (int)VerifyVerdict.Fail);
        Int32(byHand, 3);
        Text(byHand, "héllo \U0001F600");
        byHand.Add(1);
        Text(byHand, "e");
        byHand.Add(0);
        Int32(byHand, 1);
        byHand.Add(5);
        Int32(byHand, 0);
        byHand.Add(0);
        Int32(byHand, (int)VerifyVerdict.Fail);
        Int32(byHand, 3);
        Text(byHand, "x");
        Int64(byHand, 5);
        Int64(byHand, 6);
        Int32(byHand, 7);
        Text(byHand, "");

        Assert.Multiple(() =>
        {
            Assert.That(byHand.ToArray(), Is.EqualTo(MemoryPackSerializer.Serialize(result)));
            Assert.That(Result(frame => Text(frame, "AAAA")), Is.EqualTo(MemoryPackSerializer.Serialize(new VerifyResultData { Stdout = "AAAA" })));
        });
    }

    #endregion

    #region Lengths Tests

    [Test]
    public void N1ATextHeaderThatLeavesTheFrameIsRefusedTest()
    {
        // The length a text states is a claim: the number of UTF-8 bytes (written as its
        // complement), with the number of UTF-16 characters after it. Every length that is not
        // inside the frame is refused, at each end of the range a 32-bit header has, in every
        // text of both frames.
        Assert.Multiple(() =>
        {
            foreach (var header in new[] { int.MinValue, int.MinValue + 1, int.MinValue + 2, int.MinValue + 3, ~int.MaxValue + 7, ~1_000_000, ~5, -2 })
            {
                Refused(Result(frame => Raw(frame, header, 0, "AAAA"u8)), $"stdout, header {header}");
                Refused(Result(frame => Raw(frame, header, 4, "AAAA"u8)), $"stdout, header {header}, four characters stated");
                Refused(Result(frame => Text(frame, ""), stderr: frame => Raw(frame, header, 0, "AAAA"u8)), $"stderr, header {header}");
                Refused(Result(frame => Text(frame, ""), fault: frame => Raw(frame, header, 0, "AAAA"u8)), $"fault, header {header}");
                Refused(Result(frame => Text(frame, ""), cases: frame => Case(frame, text => Raw(text, header, 0, "AAAA"u8))), $"a case's stdout, header {header}");
                Refused(Head(ticket: frame => Raw(frame, header, 0, "AAAA"u8)), $"ticket, header {header}", batch: true);
                Refused(Head(diagnostics: frame => Raw(frame, header, 0, "AAAA"u8)), $"a text of the diagnostics, header {header}", batch: true);
            }

            // A count of UTF-16 characters the frame does not hold, and one whose bytes overflow 32 bits.
            foreach (var characters in new[] { 3, 1_000_000, int.MaxValue / 2 + 1, int.MaxValue })
                Refused(Result(frame => { Int32(frame, characters); frame.AddRange("AAAA"u8.ToArray()); }), $"stdout, {characters} UTF-16 characters stated");
        });
    }

    [Test]
    public void P11EveryCutOfAFrameIsRefusedTest()
    {
        // A frame that ends early, at every byte it can end at: always the one exception, never
        // another, and never a read past the end.
        var result = MemoryPackSerializer.Serialize(HonestResults().First(item => item.CaseResults.Count > 1 && item.Stdout.Length < 40));
        var head = MemoryPackSerializer.Serialize(HonestHeads().First(item => item.Diagnostics != null));

        Assert.Multiple(() =>
        {
            for (var length = 0; length < result.Length; length++)
                Refused(result[..length], $"a result cut at {length} of {result.Length}");

            for (var length = 0; length < head.Length; length++)
                Refused(head[..length], $"a head cut at {length} of {head.Length}", batch: true);

            Refused([.. result, 0], "a byte after the end of a result");
            Refused([.. head, 0], "a byte after the end of a head", batch: true);
        });
    }

    [Test]
    public void P11ATextThatIsNotTheTextItsHeaderStatesIsRefusedTest()
    {
        var two = "é"u8.ToArray();

        Assert.Multiple(() =>
        {
            // The UTF-16 length a writer states is the length the bytes decode to. One more makes
            // the decoder pad the text with NUL characters; one less makes it throw.
            Refused(Result(frame => Raw(frame, ~4, 5, "AAAA"u8)), "four bytes stated as five characters");
            Refused(Result(frame => Raw(frame, ~4, 3, "AAAA"u8)), "four bytes stated as three characters");
            Refused(Result(frame => Raw(frame, ~4, 0, "AAAA"u8)), "no number of characters stated");
            Refused(Result(frame => Raw(frame, ~4, -1, "AAAA"u8)), "a negative number of characters");
            Refused(Result(frame => Raw(frame, ~4, 12, "AAAA"u8)), "three times the bytes");
            Assert.That(() => VerifyResultFrames.CheckResult(Result(frame => Raw(frame, ~2, 1, two))), Throws.Nothing, "two bytes, one character");

            // Bytes that are not UTF-8: a lone continuation byte, a sequence cut short, an
            // overlong form, a surrogate written as three bytes.
            Refused(Result(frame => Raw(frame, ~1, 1, [0x80])), "a continuation byte alone");
            Refused(Result(frame => Raw(frame, ~1, 1, [0xC3])), "a sequence cut short");
            Refused(Result(frame => Raw(frame, ~2, 1, [0xC0, 0x80])), "an overlong form");
            Refused(Result(frame => Raw(frame, ~3, 1, [0xED, 0xA0, 0x80])), "a surrogate as three bytes");

            // The UTF-16 form: a surrogate without its pair is no character.
            Assert.That(() => VerifyResultFrames.CheckResult(Result(frame => Utf16(frame, "ok \U0001F600"))), Throws.Nothing, "a pair of surrogates is one character");
            Refused(Result(frame => Utf16(frame, "a\uD83D")), "a high surrogate at the end");
            Refused(Result(frame => Utf16(frame, "\uDE00a")), "a low surrogate first");
            Refused(Result(frame => Utf16(frame, "\uD83Da")), "a high surrogate before a letter");
            Refused(Result(frame => Text(frame, ""), stderr: frame => Utf16(frame, "\uD83D")), "stderr");

            // A text longer than its bound, in either form.
            var name = new string('n', VerifyResultFrames.MAX_NAME_LENGTH + 1);
            Refused(Result(frame => Text(frame, ""), fault: frame => Text(frame, name)), "a fault longer than a code");
            Refused(Result(frame => Text(frame, ""), fault: frame => Utf16(frame, name)), "a fault longer than a code, as UTF-16");
            Refused(Head(ticket: frame => Text(frame, name)), "a ticket longer than a ticket", batch: true);
            Refused(Result(frame => Text(frame, ""), stderr: frame => Text(frame, new string('e', VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES + 1))), "stderr over its ceiling");
            Assert.That(() => VerifyResultFrames.CheckResult(Result(frame => Text(frame, ""), stderr: frame => Text(frame, new string('e', VerifyLimitCeilings.MAX_STDERR_LIMIT_BYTES)))), Throws.Nothing, "stderr at its ceiling");
        });
    }

    [Test]
    public void N10ACountOutsideItsBoundIsRefusedTest()
    {
        // Audit findings N-3 (a count of -2 was an exception of another type, out of the
        // settlement) and N-10 (a count of 67 million nulls made the decoder allocate 640 MiB).
        Assert.Multiple(() =>
        {
            Refused(Result(frame => Text(frame, ""), cases: frame => Int32(frame, -2)), "a count of -2");
            Refused(Result(frame => Text(frame, ""), cases: frame => Int32(frame, int.MinValue)), "the smallest count");
            Refused(Result(frame => Text(frame, ""), cases: frame => Int32(frame, 67_108_800)), "67 million cases stated");
            Refused(Result(frame => Text(frame, ""), cases: frame => { Int32(frame, VerifyResultFrames.MAX_CASES + 1); frame.AddRange(Enumerable.Repeat((byte)255, VerifyResultFrames.MAX_CASES + 1)); }),
                "one case more than a suite may have, each of them no object");
            Assert.That(() => VerifyResultFrames.CheckResult(Result(frame => Text(frame, ""), cases: frame => { Int32(frame, VerifyResultFrames.MAX_CASES); frame.AddRange(Enumerable.Repeat((byte)255, VerifyResultFrames.MAX_CASES)); })),
                Throws.Nothing, "as many as a suite may have: whether a record may be missing is asked of the decoded result");
            Refused(Result(frame => Text(frame, ""), cases: frame => { Int32(frame, 1); frame.Add(4); }), "a case of four members");

            // The head of a batch carries no results: they are frames of their own.
            Refused(Head(results: 1), "a head with a result in it", batch: true);
            Refused(Head(results: 67_108_800), "a head that states 67 million results", batch: true);
            Assert.That(() => VerifyResultFrames.CheckBatch(Head(results: -1)), Throws.Nothing, "no list at all");
        });
    }

    [Test]
    public void P11AFlagThatIsNoFlagAndAnObjectOfOtherMembersAreRefusedTest()
    {
        var honest = MemoryPackSerializer.Serialize(new VerifyResultData { Stdout = "AAAA" });
        var flag = honest.AsSpan().IndexOf("AAAA"u8) + 4;

        var odd = (byte[])honest.Clone();
        odd[flag] = 2;

        var fewer = (byte[])honest.Clone();
        fewer[0] = 11;

        var more = (byte[])honest.Clone();
        more[0] = 13;

        var none = (byte[])honest.Clone();
        none[0] = 255;

        Assert.Multiple(() =>
        {
            Assert.That(honest[flag], Is.Zero, "the flag after stdout");
            Refused(odd, "a flag of 2");
            Refused(fewer, "a result of eleven members");
            Refused(more, "a result of thirteen members");
            Refused(none, "no result at all");
            Refused([], "an empty frame");
            Refused([], "an empty head", batch: true);
        });
    }

    [Test]
    public void P11AFrameTheWalkPassesIsOneTheDecoderReadsTest()
    {
        // Honest frames with bytes changed at random: most are refused by the walk, and every one
        // it lets through is read by the decoder without an exception: the walk is the stricter
        // of the two.
        var random = new Random(SEED);
        var results = HonestResults().Where(item => item.Stdout.Length < 200).Select(item => MemoryPackSerializer.Serialize(item)).ToList();
        var heads = HonestHeads().Select(item => MemoryPackSerializer.Serialize(item)).ToList();

        var passed = 0;
        var refused = 0;
        var failures = new List<string>();

        for (var round = 0; round < MUTATIONS; round++)
        {
            var batch = round % 4 == 0;
            var frame = Mutate(random, batch ? heads[random.Next(heads.Count)] : results[random.Next(results.Count)]);

            try
            {
                if (batch)
                    VerifyResultFrames.CheckBatch(frame);
                else
                    VerifyResultFrames.CheckResult(frame);
            }
            catch (InvalidDataException)
            {
                refused++;
                continue;
            }

            passed++;

            try
            {
                object? read = batch ? MemoryPackSerializer.Deserialize<VerifyResultBatchData>(frame) : MemoryPackSerializer.Deserialize<VerifyResultData>(frame);
                if (read == null)
                    failures.Add($"round {round}: read as nothing: {Convert.ToHexString(frame)}");
            }
            catch (Exception exception)
            {
                failures.Add($"round {round}: {exception.GetType().Name}: {Convert.ToHexString(frame)}");
            }
        }

        TestContext.Out.WriteLine($"{MUTATIONS} changed frames: {refused} refused by the walk, {passed} passed and read");

        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(10)));
            Assert.That(refused, Is.GreaterThan(MUTATIONS / 4), "the changes reach what the walk checks");
            Assert.That(passed, Is.GreaterThan(MUTATIONS / 100), "and some leave the frame one a writer could have made");
        });
    }

    #endregion

    #region Tools

    private static void Refused(byte[] frame, string what, bool batch = false)
    {
        // Only this exception: a frame that cannot be read fails its package, and anything else
        // would be something the walk let through to throw.
        Assert.That(() =>
        {
            if (batch)
                VerifyResultFrames.CheckBatch(frame);
            else
                VerifyResultFrames.CheckResult(frame);
        }, Throws.TypeOf<InvalidDataException>(), what);
    }

    private static IEnumerable<VerifyResultData> HonestResults()
    {
        yield return new VerifyResultData();
        yield return new VerifyResultData { TaskIndex = 3, Verdict = VerifyVerdict.RuntimeError, ExitCode = -1, Stdout = "out\n", Stderr = "wasm trap: unreachable", FuelConsumed = 12345, PeakMemoryBytes = 65536, WallMs = 4 };
        yield return new VerifyResultData { TaskIndex = int.MaxValue, Verdict = VerifyVerdict.NoResult, ExitCode = -1, Fault = VerifyFault.WALL_CLOCK };
        yield return new VerifyResultData { Stdout = "héllo, мир, 世界, \U0001F600 and a NUL \0 and a tab \t", StdoutTruncated = true, Stderr = "\U0001F600", StderrTruncated = true };
        yield return new VerifyResultData
        {
            TaskIndex = 5, Verdict = VerifyVerdict.Fail, ExitCode = 2, Stdout = "last\n", FuelConsumed = long.MaxValue, PeakMemoryBytes = long.MaxValue, WallMs = int.MaxValue,
            CaseResults =
            [
                new VerifyCaseResultData { CaseIndex = 0, Passed = true, Verdict = VerifyVerdict.Pass, ActualStdout = "" },
                new VerifyCaseResultData { CaseIndex = 1, Passed = false, Verdict = VerifyVerdict.Fail, ExitCode = 2, ActualStdout = "ünïcödé" },
                new VerifyCaseResultData { CaseIndex = 2, Passed = true, Verdict = VerifyVerdict.Pass, ActualStdout = "last\n" }
            ]
        };
        yield return new VerifyResultData { Stdout = new string('x', 100_000), Stderr = new string('é', 30_000) };
    }

    private static IEnumerable<VerifyResultBatchData> HonestHeads()
    {
        yield return new VerifyResultBatchData();
        yield return new VerifyResultBatchData { Ticket = new string('a', 32) };
        yield return new VerifyResultBatchData
        {
            Ticket = "0123456789abcdef0123456789abcdef",
            Diagnostics = new VerifyBatchDiagnosticsData
            {
                RuntimeTarget = "linux-x64", WasmtimeVersion = "48.0.8.0 (native 0123456789ab)", RuntimeId = "python-3.14.6", RuntimeSha256 = new string('c', 64),
                TaskCount = 32, ControllerVersion = "0.16.0"
            }
        };
    }

    private static int Members(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Count(property => property.GetCustomAttribute<MemoryPackOrderAttribute>() != null);
    }

    /// <summary>A result frame written by hand, with the parts a test names written its way and the rest as a writer writes them.</summary>
    private static byte[] Result(Action<List<byte>> stdout, Action<List<byte>>? stderr = null, Action<List<byte>>? cases = null, Action<List<byte>>? fault = null)
    {
        var frame = new List<byte> { 12 };
        Int32(frame, 0);
        Int32(frame, 0);
        Int32(frame, 0);
        stdout(frame);
        frame.Add(0);
        (stderr ?? (text => Text(text, "")))(frame);
        frame.Add(0);
        (cases ?? (list => Int32(list, 0)))(frame);
        Int64(frame, 0);
        Int64(frame, 0);
        Int32(frame, 0);
        (fault ?? (text => Text(text, "")))(frame);
        return frame.ToArray();
    }

    /// <summary>One case record in a list of one.</summary>
    private static void Case(List<byte> frame, Action<List<byte>> stdout)
    {
        Int32(frame, 1);
        frame.Add(5);
        Int32(frame, 0);
        frame.Add(1);
        Int32(frame, 0);
        Int32(frame, 0);
        stdout(frame);
    }

    /// <summary>The head of a batch written by hand.</summary>
    private static byte[] Head(int results = 0, Action<List<byte>>? diagnostics = null, Action<List<byte>>? ticket = null)
    {
        var frame = new List<byte> { 3 };
        Int32(frame, results);

        if (diagnostics == null)
        {
            frame.Add(255);
        }
        else
        {
            frame.Add(11);
            frame.AddRange(new byte[16]);
            Text(frame, "");
            Text(frame, "linux-x64");
            diagnostics(frame);
            Text(frame, "");
            Text(frame, "wasi");
            Text(frame, "");
            Int32(frame, 0);
            Int32(frame, 1);
            Int32(frame, 0);
            Text(frame, "0.16.0");
        }

        (ticket ?? (text => Text(text, new string('a', 32))))(frame);
        return frame.ToArray();
    }

    private static void Int32(List<byte> frame, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        frame.AddRange(bytes.ToArray());
    }

    private static void Int64(List<byte> frame, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        frame.AddRange(bytes.ToArray());
    }

    /// <summary>A text as a writer writes it: the complement of its UTF-8 length, its UTF-16 length, its bytes.</summary>
    private static void Text(List<byte> frame, string text)
    {
        if (text.Length == 0)
        {
            Int32(frame, 0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        Raw(frame, ~bytes.Length, text.Length, bytes);
    }

    /// <summary>A text with a header and a number of characters of the test's choosing.</summary>
    private static void Raw(List<byte> frame, int header, int characters, ReadOnlySpan<byte> bytes)
    {
        Int32(frame, header);
        Int32(frame, characters);
        frame.AddRange(bytes.ToArray());
    }

    /// <summary>A text in the UTF-16 form: its number of characters, then the characters.</summary>
    private static void Utf16(List<byte> frame, string text)
    {
        Int32(frame, text.Length);

        // Unit by unit: an encoder would put a replacement character where a surrogate stands alone.
        foreach (var unit in text)
        {
            frame.Add((byte)unit);
            frame.Add((byte)(unit >> 8));
        }
    }

    private static byte[] Mutate(Random random, byte[] source)
    {
        var frame = (byte[])source.Clone();
        if (frame.Length == 0)
            return frame;

        switch (random.Next(6))
        {
            case 0:
                frame[random.Next(frame.Length)] = (byte)random.Next(256);
                break;
            case 1:
                frame[random.Next(frame.Length)] ^= (byte)(1 << random.Next(8));
                break;
            case 2:
                if (frame.Length >= sizeof(int))
                {
                    int[] numbers = [int.MinValue, int.MinValue + 1, -2, -1, 0, 1, 2, 255, 1024, 1025, 65536, int.MaxValue, ~frame.Length, frame.Length, random.Next()];
                    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(random.Next(frame.Length - sizeof(int) + 1)), numbers[random.Next(numbers.Length)]);
                }

                break;
            case 3:
                return frame[..random.Next(frame.Length)];
            case 4:
                return [.. frame, .. new byte[random.Next(1, 9)]];
            default:
                var from = random.Next(frame.Length);
                var count = random.Next(1, Math.Min(8, frame.Length - from) + 1);
                random.NextBytes(frame.AsSpan(from, count));
                break;
        }

        return frame;
    }

    #endregion
}
