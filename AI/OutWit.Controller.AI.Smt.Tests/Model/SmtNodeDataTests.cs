using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// The wire types of a package as values: a package of problems, a row, a package of results and
/// the diagnostics travel through MemoryPack whole, compare by every member and copy deep; a row's
/// digest covers every value that is a function of the problem and leaves the machine's out; the
/// words and the codes are fixed lists.
/// </summary>
[TestFixture]
public sealed class SmtNodeDataTests
{
    #region Task Package Tests

    [Test]
    public void TaskPackageRoundTripsAndComparesEveryMemberTest()
    {
        var package = TaskPackage();
        var back = MemoryPackSerializer.Deserialize<SmtTaskPackageData>(MemoryPackSerializer.Serialize(package))!;

        var changes = new Action<SmtTaskPackageData>[]
        {
            other => other.ProblemsBlobId = Guid.NewGuid(),
            other => other.Sha256 = new string('b', 64),
            other => other.SizeBytes = 2,
            other => other.ProblemCount = 4,
            other => other.Ticket = new string('f', 32),
            other => other.RequiredControllerVersion = "9.9.9",
            other => other.IndexDigest = new string('d', 64),
            other => other.Options!.Seed = 8,
            other => other.Options = null
        };

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(package), Is.True);
            Assert.That(back.Options!.Is(package.Options!), Is.True, "the options travel with the package");
            Assert.That(package.Clone().Is(package), Is.True);
            Assert.That(package.Clone().Options, Is.Not.SameAs(package.Options), "a deep copy");
            Assert.That(new SmtTaskPackageData().Is(new SmtTaskPackageData()), Is.True);

            foreach (var change in changes)
            {
                var other = package.Clone();
                change(other);
                Assert.That(other.Is(package), Is.False);
                Assert.That(package.Is(TaskPackage()), Is.True, "a change of the copy does not reach the original");
            }
        });
    }

    [Test]
    public void TaskPackageSaysWhichOneItIsWithoutItsTicketTest()
    {
        var text = TaskPackage().ToString();

        Assert.That(text, Does.Contain("3").And.Contain("0.2.0"));
        Assert.That(text, Does.Not.Contain(TaskPackage().Ticket), "a ticket is never written where it can be read");
    }

    #endregion

    #region Result Package Tests

    [Test]
    public void ResultPackageRoundTripsAndComparesEveryMemberTest()
    {
        var package = ResultPackage();
        var back = MemoryPackSerializer.Deserialize<SmtResultPackageData>(MemoryPackSerializer.Serialize(package))!;

        var changes = new Action<SmtResultPackageData>[]
        {
            other => other.ResultsBlobId = Guid.NewGuid(),
            other => other.Sha256 = "",
            other => other.SizeBytes = 0,
            other => other.ResultCount = 1,
            other => other.Ticket = "",
            other => other.FaultCode = SmtNodeFaults.UPLOAD_FAILED,
            other => other.FaultMessage = "x"
        };

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(package), Is.True);
            Assert.That(package.Clone().Is(package), Is.True);
            foreach (var change in changes)
            {
                var other = package.Clone();
                change(other);
                Assert.That(other.Is(package), Is.False);
            }
        });
    }

    #endregion

    #region Row Tests

    [Test]
    public void RowRoundTripsAndComparesEveryMemberTest()
    {
        var row = Row();
        var back = MemoryPackSerializer.Deserialize<SmtResultData>(MemoryPackSerializer.Serialize(row))!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(row), Is.True);
            Assert.That(back.Models, Is.EqualTo(row.Models));
            Assert.That(back.Values, Is.EqualTo(row.Values));
            Assert.That(row.Clone().Is(row), Is.True);
            Assert.That(row.Clone().Models, Is.Not.SameAs(row.Models), "a deep copy");

            foreach (var (name, change) in Changes())
            {
                var other = row.Clone();
                change(other);
                Assert.That(other.Is(row), Is.False, name);
            }
        });
    }

    [Test]
    public void TheDigestCoversEveryValueOfTheProblemAndNothingOfTheMachineTest()
    {
        var row = Row();
        var digest = SmtResultDigest.Of(row);

        // The machine's, the job's and the package's: the same problem on another machine, in another package.
        var elsewhere = row.Clone();
        elsewhere.WallMs = 99_999;
        elsewhere.Platform = "osx-arm64";
        elsewhere.ControllerVersion = "0.2.1";
        elsewhere.Ticket = new string('e', 32);
        elsewhere.Digest = "anything";

        Assert.Multiple(() =>
        {
            Assert.That(digest, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(SmtResultDigest.Of(elsewhere), Is.EqualTo(digest), "the wall time, the platform, the version, the ticket and the digest are left out");

            // Every other value is in it.
            foreach (var (name, change) in Changes().Where(change => change.Name is not ("WallMs" or "Platform" or "ControllerVersion" or "Ticket" or "Digest")))
            {
                var other = row.Clone();
                change(other);
                Assert.That(SmtResultDigest.Of(other), Is.Not.EqualTo(digest), name);
            }
        });
    }

    [Test]
    public void TheCanonicalFormTellsTextsApartByTheirLengthsTest()
    {
        // Two models that read as one text joined, and one model of that text: another form.
        var two = Row();
        two.Models = ["(a)\nmodel[3]=(b)", "x"];
        var one = Row();
        one.Models = ["(a)", "(b)\nmodel[1]=x"];

        Assert.Multiple(() =>
        {
            Assert.That(SmtResultDigest.Canonical(two), Is.Not.EqualTo(SmtResultDigest.Canonical(one)));
            Assert.That(SmtResultDigest.Canonical(Row()), Does.StartWith(SmtResultDigest.FORMAT + "\n"));
            Assert.That(SmtResultDigest.Canonical(Row()), Does.Contain("\nquery=enumerate\n"));
        });
    }

    [Test]
    public void ARowHoldsTheDigestOfItsOwnValuesOrSaysItDoesNotTest()
    {
        var row = Row();
        row.Digest = SmtResultDigest.Of(row);
        var forged = row.Clone();
        forged.Verdict = SmtResultWords.UNSAT;

        Assert.Multiple(() =>
        {
            Assert.That(SmtResultDigest.Holds(row), Is.True);
            Assert.That(SmtResultDigest.Holds(forged), Is.False, "a row whose values changed after its digest was made");
        });
    }

    [Test]
    public void RowSaysWhichProblemItAnswersWithoutItsModelsOrItsTicketTest()
    {
        var text = Row().ToString();

        Assert.That(text, Does.Contain("cage-1").And.Contain("ok").And.Contain("sat"));
        Assert.That(text, Does.Not.Contain("define-fun"), "a model is never in a log line");
        Assert.That(text, Does.Not.Contain(Row().Ticket));
    }

    #endregion

    #region Words Tests

    [Test]
    public void TheWordsOfARowAreFixedListsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SmtResultWords.STATUSES, Is.EqualTo(new[] { "ok", "refused", "input_error", "output_exceeded", "engine_crash", "node_fault" }));
            Assert.That(SmtResultWords.VERDICTS, Is.EqualTo(new[] { "sat", "unsat", "unknown", "none" }));
            Assert.That(SmtResultWords.REASONS, Is.EqualTo(new[] { "rlimit", "fuel", "memout", "incomplete", "other", "none" }));
            Assert.That(SmtResultWords.PROOFS, Is.EqualTo(new[] { "proved", "counterexample", "none" }));
            Assert.That(SmtNodeFaults.ALL, Is.Unique);
            Assert.That(SmtNodeFaults.ALL, Has.All.Match("^[a-z_]+$"));
        });
    }

    #endregion

    #region Diagnostics Tests

    [Test]
    public void DiagnosticsRoundTripAndCompareEveryMemberTest()
    {
        var diagnostics = new SmtRuntimeDiagnosticsData
        {
            ControllerVersion = "0.2.0", ProfileId = "z3-5.4.1", SolverVersion = "5.4.1", ModuleFile = "wasm/z3-5.4.1.wasm", ModuleSha256 = new string('a', 64),
            ModuleSize = 30_204_160, ModulePresent = true, ModuleCompiled = true, CompileMs = 13_000, SandboxProblem = "", WasmtimeBinding = "48.0.2.0",
            NativesVersion = "48.0.8", NativesSha256 = new string('b', 64), NativesPinned = true, TrapMode = "signals", Platform = "linux-x64",
            LogicalCores = 8, AvailableMemoryBytes = 16L << 30, SessionMemoryBytes = 2_818_572_288, Degree = 4, DegreeReason = "memory"
        };

        var back = MemoryPackSerializer.Deserialize<SmtRuntimeDiagnosticsData>(MemoryPackSerializer.Serialize(diagnostics))!;
        var other = diagnostics.Clone();
        other.Degree = 3;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(diagnostics), Is.True);
            Assert.That(diagnostics.Clone().Is(diagnostics), Is.True);
            Assert.That(other.Is(diagnostics), Is.False);
        });
    }

    #endregion

    #region Tools

    private static SmtTaskPackageData TaskPackage()
    {
        return new SmtTaskPackageData
        {
            ProblemsBlobId = new Guid("11111111-2222-3333-4444-555555555555"),
            Sha256 = new string('a', 64),
            SizeBytes = 1_234,
            ProblemCount = 3,
            Ticket = "0123456789abcdef0123456789abcdef",
            RequiredControllerVersion = "0.2.0",
            IndexDigest = new string('c', 64),
            Options = new SmtOptionsData { Seed = 7, HardCapSeconds = 60, MemoryMegabytes = 1_024, MaxModels = 2 }
        };
    }

    private static SmtResultPackageData ResultPackage()
    {
        return new SmtResultPackageData
        {
            ResultsBlobId = new Guid("aaaaaaaa-2222-3333-4444-555555555555"),
            Sha256 = new string('a', 64),
            SizeBytes = 99,
            ResultCount = 3,
            Ticket = "0123456789abcdef0123456789abcdef",
            FaultCode = "",
            FaultMessage = ""
        };
    }

    private static SmtResultData Row()
    {
        return new SmtResultData
        {
            Index = 12,
            Id = "cage-1",
            Hash = new string('a', 64),
            Ticket = "0123456789abcdef0123456789abcdef",
            Query = SmtQuery.Enumerate,
            Status = SmtResultWords.OK,
            Code = "",
            Message = "",
            Verdict = SmtResultWords.SAT,
            Reason = SmtResultWords.NONE,
            ReasonText = "",
            Proof = SmtResultWords.NONE,
            Count = 2,
            Complete = false,
            LimitReached = true,
            Models = ["(\n  (define-fun a () Int 1)\n)", "(\n  (define-fun a () Int 2)\n)"],
            Values = ["((a 1))", "((a 2))"],
            Checks = 2,
            ResourceCount = 12_345,
            Fuel = 67_890,
            PeakMemoryBytes = 18_874_368,
            Transcript = "> (check-sat)\nsat\n",
            ControllerVersion = "0.2.0",
            Platform = "win-x64",
            WallMs = 12,
            Digest = ""
        };
    }

    /// <summary>One change of each member of a row, by the member's name.</summary>
    private static IEnumerable<(string Name, Action<SmtResultData> Change)> Changes()
    {
        yield return ("Index", other => other.Index = 13);
        yield return ("Id", other => other.Id = "cage-2");
        yield return ("Hash", other => other.Hash = new string('b', 64));
        yield return ("Ticket", other => other.Ticket = new string('f', 32));
        yield return ("Query", other => other.Query = SmtQuery.Check);
        yield return ("Status", other => other.Status = SmtResultWords.NODE_FAULT);
        yield return ("Code", other => other.Code = "cancelled");
        yield return ("Message", other => other.Message = "m");
        yield return ("Verdict", other => other.Verdict = SmtResultWords.UNSAT);
        yield return ("Reason", other => other.Reason = SmtResultWords.FUEL);
        yield return ("ReasonText", other => other.ReasonText = "canceled");
        yield return ("Proof", other => other.Proof = SmtResultWords.PROVED);
        yield return ("Count", other => other.Count = 3);
        yield return ("Complete", other => other.Complete = true);
        yield return ("LimitReached", other => other.LimitReached = false);
        yield return ("Models", other => other.Models.RemoveAt(1));
        yield return ("Models", other => other.Models[0] = "(\n  (define-fun a () Int 3)\n)");
        yield return ("Values", other => other.Values.Add("((a 3))"));
        yield return ("Checks", other => other.Checks = 3);
        yield return ("ResourceCount", other => other.ResourceCount = 1);
        yield return ("Fuel", other => other.Fuel = 1);
        yield return ("PeakMemoryBytes", other => other.PeakMemoryBytes = 1);
        yield return ("Transcript", other => other.Transcript = "");
        yield return ("ControllerVersion", other => other.ControllerVersion = "0.2.1");
        yield return ("Platform", other => other.Platform = "linux-x64");
        yield return ("WallMs", other => other.WallMs = 13);
        yield return ("Digest", other => other.Digest = "d");
    }

    #endregion
}
