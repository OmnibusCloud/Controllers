using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Delivery;

/// <summary>
/// What a node refuses of a package, with the code of each refusal: before the download, a
/// reference made for another version, without a ticket in its form, a blob, options, problems or
/// a size, or with a blob larger than a node takes; after it, a blob of another size or SHA-256
/// than its reference names, or one that holds other problems; and then a node without a sandbox
/// or without memory for one session. No solver runs here.
/// </summary>
[TestFixture]
public sealed class SmtPackageRefusalTests
{
    #region Fields

    private string m_folder = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_folder = Path.Combine(Path.GetTempPath(), $"witai_smt_refusal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_folder);
    }

    [TearDown]
    public void TearDown()
    {
        SmtRigModules.TryDelete(m_folder);
    }

    #endregion

    #region Blob Tests

    [Test]
    public void ABlobThatIsNotTheOneItsReferenceNamesIsRefusedWithItsCodeTest()
    {
        var path = File("p.wsp");
        var package = SmtNodeRig.Write(Problems(3), new SmtOptionsData(), path);

        var resized = package.Clone();
        resized.SizeBytes++;

        var rehashed = package.Clone();
        rehashed.Sha256 = new string('0', 64);

        var counted = package.Clone();
        counted.ProblemCount = 4;

        var shorter = package.Clone();
        shorter.ProblemCount = 2;

        var others = package.Clone();
        others.IndexDigest = SmtNodeRig.IndexDigest([0, 1, 7]);

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.BlobRefusal(package, path), Is.Null);
            Assert.That(SmtNodeRig.BlobRefusal(resized, path)?.Code, Is.EqualTo(SmtNodeFaults.PACKAGE_HASH_MISMATCH), "another size");
            Assert.That(SmtNodeRig.BlobRefusal(rehashed, path)?.Code, Is.EqualTo(SmtNodeFaults.PACKAGE_HASH_MISMATCH), "another SHA-256");
            Assert.That(SmtNodeRig.BlobRefusal(counted, path)?.Code, Is.EqualTo(SmtNodeFaults.PACKAGE_MISMATCH), "fewer problems than it names");
            Assert.That(SmtNodeRig.BlobRefusal(shorter, path)?.Code, Is.EqualTo(SmtNodeFaults.PACKAGE_MISMATCH), "more problems than it names");
            Assert.That(SmtNodeRig.BlobRefusal(others, path)?.Code, Is.EqualTo(SmtNodeFaults.PACKAGE_MISMATCH), "other problems than it names");
        });
    }

    #endregion

    #region Reference Tests

    [Test]
    public void AReferenceANodeDoesNotTakeIsRefusedBeforeTheDownloadWithItsCodeTest()
    {
        var good = Reference();
        var cases = new (string What, Action<SmtTaskPackageData> Change, string Code)[]
        {
            ("another version", package => package.RequiredControllerVersion = "0.1.9", SmtNodeFaults.VERSION_MISMATCH),
            ("no version", package => package.RequiredControllerVersion = "", SmtNodeFaults.VERSION_MISMATCH),
            ("no ticket", package => package.Ticket = "", SmtNodeFaults.TICKET_INVALID),
            ("a ticket in capitals", package => package.Ticket = package.Ticket.ToUpperInvariant(), SmtNodeFaults.TICKET_INVALID),
            ("a ticket too short", package => package.Ticket = package.Ticket[..31], SmtNodeFaults.TICKET_INVALID),
            ("no blob", package => package.ProblemsBlobId = Guid.Empty, SmtNodeFaults.PACKAGE_INVALID),
            ("no options", package => package.Options = null, SmtNodeFaults.PACKAGE_INVALID),
            ("no problems", package => package.ProblemCount = 0, SmtNodeFaults.PACKAGE_INVALID),
            ("too many problems", package => package.ProblemCount = SmtNodeRig.MaxPackageProblems + 1, SmtNodeFaults.PACKAGE_INVALID),
            ("no size", package => package.SizeBytes = 0, SmtNodeFaults.PACKAGE_INVALID),
            ("an oversized blob", package => package.SizeBytes = SmtNodeRig.MaxPackageBytes + 1, SmtNodeFaults.PACKAGE_TOO_LARGE)
        };

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Refusal(good), Is.Null, "a good reference is taken");
            foreach (var (what, change, code) in cases)
            {
                var package = good.Clone();
                change(package);
                Assert.That(SmtNodeRig.Refusal(package)?.Code, Is.EqualTo(code), what);
            }
        });
    }

    [Test]
    public void AnotherVersionIsNamedInFixedWordsTest()
    {
        var package = Reference();
        package.RequiredControllerVersion = "0.1.9\n/home/someone";

        var message = SmtNodeRig.Refusal(package)!.Value.Message;

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain($"this node runs Smt {SmtNodeRig.ControllerVersion}"));
            Assert.That(message, Does.Contain("Smt 0.1.9?"), "what the package names, cut to a version's characters");
            Assert.That(message, Does.Not.Contain("/home"));
        });
    }

    [Test]
    public void ANodeWithoutASandboxOrWithoutMemoryForOneSessionRefusesThePackageTest()
    {
        var package = Reference();
        var big = Reference();
        big.Options = new SmtOptionsData { MemoryMegabytes = 4_096 };

        Assert.Multiple(() =>
        {
            Assert.That(SmtNodeRig.Prepare(package, "the solver module is not in the controller's folder", 8, 16L << 30).Fault, Is.EqualTo(SmtNodeFaults.SANDBOX_UNAVAILABLE));
            Assert.That(SmtNodeRig.Prepare(big, null, 8, 4L << 30).Fault, Is.EqualTo(SmtNodeFaults.MEMORY_INSUFFICIENT), "two caps of 4 GiB do not fit in three quarters of 4 GiB");
            Assert.That(SmtNodeRig.Prepare(big, null, 8, 32L << 30), Is.EqualTo((2, (string?)null)), "they fit twice in 24 GiB");
            Assert.That(SmtNodeRig.Prepare(package, null, 8, 32L << 30), Is.EqualTo((8, (string?)null)));
        });
    }

    #endregion

    #region Tools

    private string File(string name)
    {
        return Path.Combine(m_folder, name);
    }

    private static SmtTaskPackageData Reference()
    {
        return new SmtTaskPackageData
        {
            ProblemsBlobId = Guid.NewGuid(),
            Sha256 = new string('a', 64),
            SizeBytes = 1_000,
            ProblemCount = 3,
            Ticket = "0123456789abcdef0123456789abcdef",
            RequiredControllerVersion = SmtNodeRig.ControllerVersion,
            IndexDigest = SmtNodeRig.IndexDigest([0, 1, 2]),
            Options = new SmtOptionsData()
        };
    }

    private static List<SmtProblemData> Problems(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => SmtGoldenPackages.Indexed(SmtSolverProblems.Check($"p{index}", $"(set-logic QF_LIA)(declare-const x Int)(assert (> x {index}))", "x"), index))
            .ToList();
    }

    #endregion
}
