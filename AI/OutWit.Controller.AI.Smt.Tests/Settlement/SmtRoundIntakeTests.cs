using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Tests.Mock;
using OutWit.Controller.AI.Smt.Tests.Run;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// What the host does with a result that is not one, a package at a time: a blob returned twice
/// counts once; a blob that is no package of results, is not the bytes its reference names, is not
/// in the store, or is larger than its rows may be is unreadable; a reference whose own words are
/// not a node's is refused before anything is fetched; the rows of a refused package leave nothing
/// in the host's spool; and what the host keeps of a run has a bound, past which the settlement
/// fails by name. A fault of the host's own files is never counted as a node's: it ends the
/// activity.
/// </summary>
[TestFixture]
public sealed class SmtRoundIntakeTests
{
    #region Constants

    private const string X_TWO = "((define-fun x () Int 2))";
    private const string X_THREE = "((define-fun x () Int 3))";

    private const int MEBIBYTE = 1 << 20;
    private const char HALF_A_PAIR = (char)0xD800;
    private const char THREE_BYTES = (char)0x20AC;

    #endregion

    #region Fields

    private SmtSettleRig m_rig = null!;
    private SmtIntakeRig m_intake = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_rig = new SmtSettleRig();
        m_intake = new SmtIntakeRig();
    }

    [TearDown]
    public void TearDown()
    {
        m_intake.Dispose();
        m_rig.Dispose();
    }

    #endregion

    #region Blob Tests

    [Test]
    public void AResultBlobReturnedTwiceCountsOnceTest()
    {
        var (set, options, package) = Package(1, rate: 1);
        var answer = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));

        var run = Settle(set, options, package, [answer, answer.Clone()], rate: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Rejected(run, "duplicate"), Is.EqualTo(1));
            Assert.That(Count(run, "rejected_results"), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(1), "one blob is one answer, however often it comes back");
            Assert.That(Row(run, 0).GetProperty("settled_by").GetString(), Is.EqualTo(SmtRunWords.ONE_COPY));
        });
    }

    [Test]
    public void ABlobThatIsNoPackageOfResultsIsUnreadableTest()
    {
        var (set, options, package) = Package(1, rate: 0);
        var noise = Stored(Enumerable.Range(0, 4_096).Select(index => (byte)(index * 31)).ToArray(), package.Ticket);

        // The host's own package of problems, returned as if it were the rows: a package of another kind.
        var problems = new SmtResultPackageData { ResultsBlobId = package.ProblemsBlobId, Sha256 = package.Sha256, SizeBytes = package.SizeBytes, ResultCount = 1, Ticket = package.Ticket };

        var run = Settle(set, options, package, [noise, problems], rate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Rejected(run, "unreadable"), Is.EqualTo(2));
            Assert.That(Row(run, 0).GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.NO_RESULT), "neither is an answer");
        });
    }

    [Test]
    public void ABlobThatIsNotTheBytesItsReferenceNamesIsUnreadableTest()
    {
        var (set, options, package) = Package(1, rate: 1);
        var honest = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));
        var swapped = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));

        // Another package of results, whole and well made, under the blob the reference names: only the hash tells.
        var other = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE));
        m_rig.Blobs.Overwrite(swapped.ResultsBlobId, File.ReadAllBytes(m_rig.Blobs.GetLocalPathAsync(other.ResultsBlobId).GetAwaiter().GetResult()));

        var run = Settle(set, options, package, [honest, swapped], rate: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Rejected(run, "unreadable"), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("copies").GetInt32(), Is.EqualTo(1));
            Assert.That(Row(run, 0).GetProperty("model").GetString(), Is.EqualTo(X_TWO), "the honest copy stands alone");
            Assert.That(Count(run, "disputes"), Is.Zero, "a refused package is no copy to disagree with");
        });
    }

    [Test]
    public void ABlobTheStoreDoesNotHoldIsUnreadableTest()
    {
        var (set, options, package) = Package(1, rate: 0);
        var gone = new SmtResultPackageData { ResultsBlobId = Guid.NewGuid(), Sha256 = new string('a', 64), SizeBytes = 100, ResultCount = 1, Ticket = package.Ticket };

        var run = Settle(set, options, package, [gone], rate: 0);

        Assert.That(Rejected(run, "unreadable"), Is.EqualTo(1));
    }

    [Test]
    public void ABlobLargerThanItsRowsMayBeIsUnreadableTest()
    {
        var (set, options, package) = Package(1, rate: 0);
        var large = Stored(new byte[SmtRowCheckRig.MaxResultFrameBytes + MEBIBYTE], package.Ticket);

        var run = Settle(set, options, package, [large], rate: 0);

        Assert.That(Rejected(run, "unreadable"), Is.EqualTo(1));
    }

    [Test]
    public void ARowOfMoreBytesThanASessionWritesIsRefusedAtItsFrameTest()
    {
        // Seventeen Mi characters of three bytes each: a blob of kilobytes whose one frame is past
        // everything a session may write at three bytes a character. The reader stops at the
        // frame's head; the row is never decoded.
        var (_, _, package) = Package(1, rate: 0);
        var bomb = m_rig.Answer(package, _ => SmtSettleRig.Sat(new string(THREE_BYTES, 17 * MEBIBYTE)));

        var taken = m_intake.Main([package], [bomb], m_rig.Blobs);

        Assert.Multiple(() =>
        {
            Assert.That(new FileInfo(m_rig.Blobs.GetLocalPathAsync(bomb.ResultsBlobId).GetAwaiter().GetResult()).Length, Is.LessThan(MEBIBYTE), "the blob is small");
            Assert.That(taken.Accepted, Is.Zero);
            Assert.That(taken.RejectedFor("unreadable"), Is.EqualTo(1));
            Assert.That(taken.SpoolBytes, Is.Zero, "nothing of it reached the host's disk");
        });
    }

    [Test]
    public void ARowWithAModelPastASessionsOutputCapFailsItsPackageTest()
    {
        // Twenty MiB of model in a frame of twenty MiB: the frame is within its bound, the row is
        // not one a session writes, and nothing of it is hashed or kept.
        var (_, _, package) = Package(1, rate: 0);
        var large = m_rig.Answer(package, _ => SmtSettleRig.Sat(new string('m', 20 * MEBIBYTE)));

        var taken = m_intake.Main([package], [large], m_rig.Blobs);

        Assert.Multiple(() =>
        {
            Assert.That(taken.Accepted, Is.Zero);
            Assert.That(taken.RejectedFor("content"), Is.EqualTo(1));
            Assert.That(taken.SpoolBytes, Is.Zero);
        });
    }

    #endregion

    #region Spool Tests

    [Test]
    public void APackageRefusedAtItsLastRowLeavesTheSpoolAsItWasTest()
    {
        var (_, _, package) = Package(3, rate: 1);
        var honest = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));
        var broken = SmtRunRig.Forge(m_rig.Blobs, m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE)), rows => rows[^1].Digest = new string('0', 64));
        var shortOfARow = SmtRunRig.Forge(m_rig.Blobs, m_rig.Answer(package, _ => SmtSettleRig.Sat(X_THREE)), rows => rows.RemoveAt(rows.Count - 1));

        var alone = m_intake.Main([package], [honest], m_rig.Blobs);
        var withTheRefused = m_intake.Main([package], [honest, broken, shortOfARow], m_rig.Blobs);
        var onlyTheRefused = m_intake.Main([package], [broken, shortOfARow], m_rig.Blobs);

        Assert.Multiple(() =>
        {
            Assert.That(alone.Accepted, Is.EqualTo(1));
            Assert.That(alone.SpoolBytes, Is.GreaterThan(0), "the rows of a package that holds are kept");
            Assert.That(withTheRefused.Accepted, Is.EqualTo(1));
            Assert.That(withTheRefused.RejectedFor("content"), Is.EqualTo(2), "a row whose digest is not its own, and a package short of a row");
            Assert.That(withTheRefused.SpoolBytes, Is.EqualTo(alone.SpoolBytes), "the rows read before the package failed are gone again");
            Assert.That(onlyTheRefused.SpoolBytes, Is.Zero);
        });
    }

    [Test]
    public void ASpoolThatCannotBeWrittenEndsTheIntakeTest()
    {
        // The spool is the host's own file: when it fails, the node answered well and the server did
        // not, so nothing is counted against the package and the activity ends, to be run again.
        var (_, _, package) = Package(1, rate: 0);
        var honest = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));

        Assert.That(() => m_intake.Main([package], [honest], m_rig.Blobs, spoolClosed: true), Throws.InstanceOf<ObjectDisposedException>());
    }

    [Test]
    public void RowsPastTheBoundOfARunEndTheSettlementByNameTest()
    {
        var (_, _, package) = Package(3, rate: 0);
        var honest = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));
        var whole = m_intake.Main([package], [honest], m_rig.Blobs).SpoolBytes;

        Assert.Multiple(() =>
        {
            Assert.That(m_intake.Main([package], [honest], m_rig.Blobs, rowsBytes: whole).Accepted, Is.EqualTo(1), "rows that fill the bound to the byte are kept");
            Assert.That(() => m_intake.Main([package], [honest], m_rig.Blobs, rowsBytes: whole - 1),
                Throws.Exception.With.Message.Contains(SmtIntakeRig.RowsBound).And.Message.Contains((whole - 1).ToString()), "one byte less, and the run is not settled");
        });
    }

    [Test]
    public void FullModelsPastTheBoundOfARunEndTheSettlementByNameTest()
    {
        // A model past what the results show of a row goes whole into the ledger's own section.
        var model = "(" + string.Concat(Enumerable.Range(0, 3_000).Select(index => $"(define-fun c{index} () Int 1)")) + ")";
        var problem = string.Concat(Enumerable.Range(0, 3_000).Select(index => $"(declare-const c{index} Int)(assert (> c{index} 0))"));
        var options = new SmtOptionsData { SamplingRate = 0 };
        var set = SmtSets.Of([JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = "p0", ["smt2"] = problem })]);
        var package = m_rig.Publish(SmtSettleRig.Read(set, options), options);
        var answer = m_rig.Answer(package, _ => SmtSettleRig.Sat(model));
        var path = m_rig.StoreSet(set);

        Assert.Multiple(() =>
        {
            Assert.That(model.Length, Is.GreaterThan(SmtRunRig.MaxModelBytes));
            Assert.That(m_intake.Settle(path, options, [package], [answer], m_rig.Blobs), Does.Contain("\"model_truncated\":true"), "under a host's bounds the run is settled");
            Assert.That(() => m_intake.Settle(path, options, [package], [answer], m_rig.Blobs, modelsBytes: model.Length / 2),
                Throws.Exception.With.Message.Contains(SmtIntakeRig.ModelsBound), "past the bound of the full models it is not");
            Assert.That(() => m_intake.Settle(path, options, [package], [answer], m_rig.Blobs, rowsBytes: model.Length / 2),
                Throws.Exception.With.Message.Contains(SmtIntakeRig.RowsBound), "nor past the bound of the rows");
            Assert.That(m_intake.Leftovers(), Is.Empty, "a settlement that failed left no working file behind");
        });
    }

    #endregion

    #region Reference Tests

    [Test]
    public void AFaultReferenceWithWordsNoNodeWritesIsRefusedTest()
    {
        var (set, options, package) = Package(1, rate: 0);

        SmtResultPackageData Fault(Action<SmtResultPackageData> change)
        {
            var fault = SmtRunRig.Fault(new SmtResultPackageData { Ticket = package.Ticket }, SmtNodeFaults.SANDBOX_UNAVAILABLE);
            change(fault);
            return fault;
        }

        var returned = new[]
        {
            Fault(fault => fault.FaultMessage = new string('w', SmtResultWords.MAX_MESSAGE_CHARACTERS + 1)),
            Fault(fault => fault.FaultMessage = "this node " + HALF_A_PAIR),
            Fault(fault => fault.FaultMessage = null!),
            Fault(fault => fault.Sha256 = new string('a', 64)),
            Fault(fault => fault.FaultCode = "a word of the node's own"),
            Fault(fault => fault.FaultCode = new string('c', 100_000))
        };

        var run = Settle(set, options, package, returned, rate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Rejected(run, "content"), Is.EqualTo(returned.Length), "each is a refused result");
            Assert.That(Count(run, "node_faults"), Is.Zero, "and none is the word of a node that could not work on the package");
            Assert.That(run.Integrity, Does.Not.Contain("a word of the node's own").And.Not.Contain("cccc"));
        });
    }

    [Test]
    public void AFaultReferenceAsANodeWritesItIsCountedAsItsFaultTest()
    {
        var (set, options, package) = Package(1, rate: 0);
        var fault = SmtRunRig.Fault(new SmtResultPackageData { Ticket = package.Ticket }, SmtNodeFaults.MEMORY_INSUFFICIENT);
        fault.FaultMessage = new string('w', SmtResultWords.MAX_MESSAGE_CHARACTERS);

        var run = Settle(set, options, package, [fault], rate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Count(run, "node_faults"), Is.EqualTo(1));
            Assert.That(Count(run, "rejected_results"), Is.Zero);
        });
    }

    [Test]
    public void AnAnswerReferenceWithWordsNoNodeWritesIsRefusedTest()
    {
        var (set, options, package) = Package(1, rate: 0);

        SmtResultPackageData Answer(Action<SmtResultPackageData> change)
        {
            var answer = m_rig.Answer(package, _ => SmtSettleRig.Sat(X_TWO));
            change(answer);
            return answer;
        }

        var words = new[]
        {
            Answer(answer => answer.FaultMessage = "taken all the same"),
            Answer(answer => answer.FaultMessage = null!),
            Answer(answer => answer.FaultCode = null!)
        };
        var hashes = new[]
        {
            Answer(answer => answer.Sha256 = answer.Sha256.ToUpperInvariant()),
            Answer(answer => answer.Sha256 = answer.Sha256[..63]),
            Answer(answer => answer.Sha256 = answer.Sha256 + new string('0', 100_000)),
            Answer(answer => answer.Sha256 = null!)
        };
        var tickets = new[]
        {
            Answer(answer => answer.Ticket = answer.Ticket + new string('0', 100_000)),
            Answer(answer => answer.Ticket = answer.Ticket.ToUpperInvariant()),
            Answer(answer => answer.Ticket = null!)
        };

        var run = Settle(set, options, package, [.. words, .. hashes, .. tickets], rate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(Rejected(run, "content"), Is.EqualTo(words.Length), "words of a fault beside an answer");
            Assert.That(Rejected(run, "unreadable"), Is.EqualTo(hashes.Length), "a text that is no SHA-256 names no bytes");
            Assert.That(Rejected(run, "unknown_ticket"), Is.EqualTo(tickets.Length), "a text that is no ticket is none the round gave out");
            Assert.That(Row(run, 0).GetProperty("status").GetString(), Is.EqualTo(SmtRunWords.NO_RESULT), "none of them is an answer");
        });
    }

    #endregion

    #region Tools

    /// <summary>A set of problems of one shape, read with options of a rate, as one package.</summary>
    private (string Set, SmtOptionsData Options, SmtTaskPackageData Package) Package(int problems, double rate)
    {
        var options = new SmtOptionsData { SamplingRate = rate };
        var set = SmtSets.Of(Enumerable.Range(0, problems).Select(index => JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = $"p{index}", ["smt2"] = "(declare-const x Int)(assert (> x 1))" })));
        return (set, options, m_rig.Publish(SmtSettleRig.Read(set, options), options));
    }

    /// <summary>Bytes stored as a blob and returned under a ticket as if they were a package of results, with their own hash.</summary>
    private SmtResultPackageData Stored(byte[] bytes, string ticket)
    {
        var blob = m_rig.Blobs.UploadBytesAsync(bytes, "smt-results.wsp").GetAwaiter().GetResult();
        return new SmtResultPackageData
        {
            ResultsBlobId = blob,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            SizeBytes = bytes.Length,
            ResultCount = 1,
            Ticket = ticket
        };
    }

    private SmtSettledDocuments Settle(string set, SmtOptionsData options, SmtTaskPackageData package, IReadOnlyList<SmtResultPackageData> returned, double rate)
    {
        var run = m_rig.Settle(set, options, [package], returned, [], [], null, null, rate);
        SmtRunChecks.AssertSchemas(run);
        return run;
    }

    private static JsonElement Row(SmtSettledDocuments run, int index)
    {
        return run.Results.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement).Single(row => row.GetProperty("index").GetInt32() == index);
    }

    private static int Count(SmtSettledDocuments run, string name)
    {
        return JsonDocument.Parse(run.Integrity).RootElement.GetProperty("summary").GetProperty(name).GetInt32();
    }

    private static int Rejected(SmtSettledDocuments run, string reason)
    {
        return JsonDocument.Parse(run.Integrity).RootElement.GetProperty("summary").GetProperty("rejected").GetProperty(reason).GetInt32();
    }

    #endregion
}
