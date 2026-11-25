using System.Buffers.Binary;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Delivery;

/// <summary>
/// The package format both ways: problems written by a host are read back by a node as they were,
/// in order, and rows written by a node are read back by a host; a reader holds every frame to its
/// bound before it holds the frame, refuses what is not a package of its kind, and says why with a
/// fixed code. No solver runs here.
/// </summary>
[TestFixture]
public sealed class SmtPackageCodecTests
{
    #region Constants

    private const int MEGABYTE = 1 << 20;

    #endregion

    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_codec_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDown()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Problems Tests

    [Test]
    public void APackageOfProblemsIsReadBackAsItWasWrittenInOrderTest()
    {
        var problems = Problems(5);
        var path = File("p.wsp");

        var package = SmtNodeRig.Write(problems, new SmtOptionsData { Seed = 3 }, path);
        var (read, fault) = SmtNodeRig.ReadProblems(path, package.ProblemCount);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.Null);
            Assert.That(read, Has.Count.EqualTo(5));
            for (var index = 0; index < problems.Count; index++)
                Assert.That(read[index].Is(problems[index]), Is.True, $"problem {index}");

            Assert.That(package.ProblemCount, Is.EqualTo(5));
            Assert.That(package.SizeBytes, Is.EqualTo(new FileInfo(path).Length));
            Assert.That(package.Sha256, Is.EqualTo(Sha256(path)));
            Assert.That(package.IndexDigest, Is.EqualTo(SmtNodeRig.IndexDigest(problems.Select(problem => problem.Index))));
            Assert.That(SmtNodeRig.IsTicket(package.Ticket), Is.True);
            Assert.That(package.RequiredControllerVersion, Is.EqualTo(SmtNodeRig.ControllerVersion));
            Assert.That(package.Options!.Seed, Is.EqualTo(3));
            Assert.That(SmtNodeRig.BlobRefusal(package, path), Is.Null, "the node takes the blob its reference names");
        });
    }

    [Test]
    public void WhatIsNotAPackageOfProblemsIsUnreadableTest()
    {
        var empty = File("empty.wsp");
        System.IO.File.WriteAllBytes(empty, []);

        var noise = File("noise.wsp");
        System.IO.File.WriteAllBytes(noise, Enumerable.Range(0, 4096).Select(index => (byte)(index * 37 + 11)).ToArray());

        var results = File("results.wsp");
        SmtNodeRig.WriteRows(results, [Row(0)]);

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.ReadProblems(empty, 10).Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE), "an empty file");
            Assert.That(SmtNodeRig.ReadProblems(noise, 10).Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE), "bytes that are no package");
            Assert.That(SmtNodeRig.ReadProblems(results, 10).Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE), "a package of another kind");
        });
    }

    [Test]
    public void ADamagedStreamIsUnreadableTest()
    {
        var path = File("p.wsp");
        SmtNodeRig.Write(Problems(40), new SmtOptionsData(), path);

        var bytes = System.IO.File.ReadAllBytes(path);
        for (var offset = 16; offset < bytes.Length - 8; offset += 7)
            bytes[offset] ^= 0x5a;
        System.IO.File.WriteAllBytes(path, bytes);

        Assert.That(SmtNodeRig.ReadProblems(path, 40).Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE));
    }

    [Test]
    public void AFrameLongerThanAProblemMayBeIsRefusedBeforeItIsHeldTest()
    {
        // The head claims 200 MiB; the stream holds a few bytes. Nothing is allocated by the claim.
        var path = File("claim.wsp");
        SmtNodeRig.WriteFrames(path, SmtNodeRig.ProblemsKind, [(new byte[16], 200 * MEGABYTE)]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var fault = SmtNodeRig.ReadProblems(path, 10).Fault;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE));
            Assert.That(allocated, Is.LessThan(4 * MEGABYTE), "the claim cost nothing");
        });
    }

    [Test]
    public void AFrameThatClaimsMoreThanTheStreamHoldsIsUnreadableAndCostsNothingTest()
    {
        // Within the bound, longer than what follows: the buffer grows with the bytes that arrive.
        var path = File("short.wsp");
        SmtNodeRig.WriteFrames(path, SmtNodeRig.ProblemsKind, [(new byte[100], 100 * MEGABYTE)]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var fault = SmtNodeRig.ReadProblems(path, 10).Fault;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE));
            Assert.That(allocated, Is.LessThan(4 * MEGABYTE));
        });
    }

    [Test]
    public void AFrameThatIsNoProblemIsUnreadableTest()
    {
        var path = File("garbage.wsp");
        SmtNodeRig.WriteFrames(path, SmtNodeRig.ProblemsKind, [(Enumerable.Range(0, 64).Select(index => (byte)(255 - index)).ToArray(), null)]);

        Assert.That(SmtNodeRig.ReadProblems(path, 10).Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE));
    }

    [Test]
    public void AProblemThatClaimsAHugeListIsRefusedWithoutAllocatingItTest()
    {
        // A problem's image whose list of values claims two billion items in a frame of a few
        // hundred bytes: the decoder holds the count to the bytes that are there.
        var image = MemoryPackSerializer.Serialize(Problems(1)[0]);
        var lying = (byte[])image.Clone();
        var at = IndexOfValuesCount(image);
        BinaryPrimitives.WriteInt32LittleEndian(lying.AsSpan(at), int.MaxValue);

        var path = File("lying.wsp");
        SmtNodeRig.WriteFrames(path, SmtNodeRig.ProblemsKind, [(lying, null)]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var fault = SmtNodeRig.ReadProblems(path, 10).Fault;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.EqualTo(SmtNodeFaults.PACKAGE_UNREADABLE));
            Assert.That(allocated, Is.LessThan(16 * MEGABYTE), "the claimed count was never allocated");
        });
    }

    [Test]
    public void MoreProblemsThanTheReferenceNamesIsAMismatchTest()
    {
        var path = File("p.wsp");
        SmtNodeRig.Write(Problems(3), new SmtOptionsData(), path);

        var (read, fault) = SmtNodeRig.ReadProblems(path, 2);

        Assert.Multiple(() =>
        {
            Assert.That(fault, Is.EqualTo(SmtNodeFaults.PACKAGE_MISMATCH));
            Assert.That(read, Has.Count.EqualTo(2), "nothing past the count it may hold");
        });
    }

    [Test]
    public void APackageThatUnpacksToMoreThanItMayIsTooLargeTest()
    {
        var path = File("p.wsp");
        SmtNodeRig.Write(Problems(6), new SmtOptionsData(), path);
        var frame = MemoryPackSerializer.Serialize(Problems(1)[0]).Length + 4;

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.ReadProblems(path, 6, maxImageBytes: 3L * frame).Fault, Is.EqualTo(SmtNodeFaults.PACKAGE_TOO_LARGE));
            Assert.That(SmtNodeRig.ReadProblems(path, 6, maxImageBytes: 100L * frame).Fault, Is.Null, "and the same package within the bound reads");
        });
    }

    #endregion

    #region Result Tests

    [Test]
    public void RowsAreReadBackAsTheyWereWrittenModelsOfMegabytesIncludedTest()
    {
        var rows = Enumerable.Range(0, 3).Select(Row).ToList();
        rows[1].Models = [new string('m', 3 * MEGABYTE)];
        var path = File("r.wsp");

        SmtNodeRig.WriteRows(path, rows);
        var back = SmtNodeRig.ReadRows(path, 3);

        Assert.Multiple(() =>
        {
            Assert.That(back, Has.Count.EqualTo(3));
            for (var index = 0; index < rows.Count; index++)
                Assert.That(back[index].Is(rows[index]), Is.True, $"row {index}");
        });
    }

    [Test]
    public void AHostRefusesAResultPackageThatHoldsMoreThanItMayTest()
    {
        var path = File("r.wsp");
        SmtNodeRig.WriteRows(path, Enumerable.Range(0, 3).Select(Row));

        var claim = File("claim.wsp");
        SmtNodeRig.WriteFrames(claim, SmtNodeRig.ResultsKind, [(new byte[8], 65 * MEGABYTE)]);

        var problems = File("problems.wsp");
        SmtNodeRig.Write(Problems(1), new SmtOptionsData(), problems);

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidDataException>(() => SmtNodeRig.ReadRows(path, 2), "more rows than the package had problems");
            Assert.Throws<InvalidDataException>(() => SmtNodeRig.ReadRows(path, 3, maxImageBytes: 64), "more bytes than it may unpack to");
            Assert.Throws<InvalidDataException>(() => SmtNodeRig.ReadRows(claim, 3), "a frame longer than a row may be");
            Assert.Throws<InvalidDataException>(() => SmtNodeRig.ReadRows(problems, 3), "a package of another kind");
            Assert.That(SmtNodeRig.ReadRows(path, 3), Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void ARowsFrameHoldsTheMostASessionWritesAndNoMoreTest()
    {
        // The largest row a session can make, as bytes: a thousand models and their values that
        // fill the session's output cap, every character of them three bytes long, and every
        // other text of the row at its own length in such characters too.
        const char THREE_BYTES = (char)0x20AC;
        const int MODELS = 1_000;

        var cap = SmtRowCheckRig.OutputLimit;
        var each = cap / (2 * MODELS);
        var largest = Row(0);
        largest.Models = Enumerable.Range(0, MODELS).Select(_ => new string(THREE_BYTES, each)).ToList();
        largest.Values = Enumerable.Range(0, MODELS).Select(_ => new string(THREE_BYTES, each)).ToList();
        largest.Values[0] = new string(THREE_BYTES, cap - (2 * MODELS - 1) * each);
        largest.Message = new string(THREE_BYTES, SmtResultWords.MAX_MESSAGE_CHARACTERS);
        largest.ReasonText = new string(THREE_BYTES, SmtRowCheckRig.ReasonCharacters);
        largest.Transcript = new string('t', SmtRowCheckRig.TranscriptLimit);
        largest.Id = new string('i', 64);
        largest.Digest = new string('d', 64);

        var past = Row(1);
        past.Models = [new string(THREE_BYTES, cap + MEGABYTE)];

        var fits = File("largest.wsp");
        var over = File("past.wsp");
        SmtNodeRig.WriteRows(fits, [largest]);
        SmtNodeRig.WriteRows(over, [past]);

        Assert.Multiple(() =>
        {
            Assert.That(largest.Models.Sum(model => (long)model.Length) + largest.Values.Sum(value => (long)value.Length), Is.EqualTo(cap), "the session's whole output");
            Assert.That(SmtRowCheckRig.MaxResultFrameBytes, Is.EqualTo(3 * cap + 64 * 1024), "a session's output at three bytes a character, and the rest of a row");
            Assert.That(MemoryPackSerializer.Serialize(largest).Length, Is.InRange(3 * cap, SmtRowCheckRig.MaxResultFrameBytes), "the largest row is within its frame");
            Assert.That(SmtNodeRig.ReadRows(fits, 1).Single().Is(largest), Is.True, "and is read back as it was written");
            Assert.Throws<InvalidDataException>(() => SmtNodeRig.ReadRows(over, 1), "a row of more bytes than a session writes is refused at its frame");
        });
    }

    #endregion

    #region Ticket Tests

    [Test]
    public void TicketsAreRandomAndTheirDigestOfIndicesIsTheSetsTest()
    {
        var tickets = Enumerable.Range(0, 100).Select(_ => SmtNodeRig.NewTicket()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(tickets, Is.Unique);
            Assert.That(tickets, Has.All.Match("^[0-9a-f]{32}$"));
            Assert.That(SmtNodeRig.IsTicket("0123456789abcdef0123456789abcdeg"), Is.False);
            Assert.That(SmtNodeRig.IsTicket(null), Is.False);
            Assert.That(SmtNodeRig.IndexDigest([3, 1, 2]), Is.EqualTo(SmtNodeRig.IndexDigest([1, 2, 3])), "the order of the problems is not the package's");
            Assert.That(SmtNodeRig.IndexDigest([1, 2, 2]), Is.Not.EqualTo(SmtNodeRig.IndexDigest([1, 2])), "an index given twice counts twice");
            Assert.That(SmtNodeRig.IndexDigest([1, 2, 3]), Is.Not.EqualTo(SmtNodeRig.IndexDigest([1, 2, 4])));
        });
    }

    #endregion

    #region Tools

    private string File(string name)
    {
        return Path.Combine(m_folder, name);
    }

    private static List<SmtProblemData> Problems(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Check($"p{index}", $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {index}))", "x"), index))
            .ToList();
    }

    private static SmtResultData Row(int index)
    {
        return new SmtResultData
        {
            Index = index, Id = $"p{index}", Hash = new string('a', 64), Ticket = SmtNodeRig.TICKET, Status = SmtResultWords.OK, Verdict = SmtResultWords.SAT,
            Reason = SmtResultWords.NONE, Proof = SmtResultWords.NONE, Models = [$"(\n  (define-fun x () Int {index + 1})\n)"], Values = [$"((x {index + 1}))"],
            Checks = 1, Fuel = 1_000 + index, Transcript = "> (check-sat)\nsat\n", ControllerVersion = SmtNodeRig.ControllerVersion, Platform = "linux-x64"
        };
    }

    /// <summary>Where the count of a problem's list of values is in its image: the member after the id, the query, the text and the hash.</summary>
    private static int IndexOfValuesCount(byte[] image)
    {
        // The image of a problem with one value: the count is the four bytes that hold 1 right before the value's text header.
        var probe = Problems(1)[0];
        var without = probe.Clone();
        without.Values = [];
        var other = MemoryPackSerializer.Serialize(without);

        // The two images agree up to the values' count; there the one holds 1 and the other 0.
        var at = 0;
        while (at < other.Length && image[at] == other[at])
            at++;

        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at)), Is.EqualTo(1), "the count of values found in the image");
        return at;
    }

    private static string Sha256(string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    #endregion
}
