using System.Reflection;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tests.Model;

/// <summary>
/// Freezes the wire layout of every MemoryPack DTO (the repo's append-only wire
/// discipline): member counts and contiguous [MemoryPackOrder] from 0. A failing case
/// means a member was inserted/reordered/removed — a wire break for the deployed
/// fleet. New members go at the END with the next order value; then update the
/// expected count here.
/// </summary>
[TestFixture]
public class VerifyModelWireLayoutTests
{
    [Test]
    public void PinListCoversAllMemoryPackableDtosTest()
    {
        // The append-only discipline is only as good as this list: a new
        // [MemoryPackable] DTO must be added to the pinned cases below.
        var pinned = new[]
        {
            typeof(VerifyLimitsData), typeof(VerifySourceFileData), typeof(VerifySuiteCaseData),
            typeof(VerifySuiteData), typeof(VerifyTaskData), typeof(VerifyTaskBatchData),
            typeof(VerifyCaseResultData), typeof(VerifyResultData), typeof(VerifyResultBatchData),
            typeof(VerifyOptionsData), typeof(VerifyRuntimeInfoData), typeof(VerifyRuntimeDiagnosticsData),
            typeof(VerifyPreflightData), typeof(VerifyBatchDiagnosticsData),
            typeof(VerifyTaskPackageData), typeof(VerifyResultPackageData)
        };

        var actual = typeof(VerifyTaskData).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute"))
            .ToArray();

        Assert.That(actual, Is.EquivalentTo(pinned),
            "a MemoryPackable DTO exists that is not wire-layout-pinned (or a pinned one was removed)");
    }

    [TestCase(typeof(VerifyLimitsData), 5)]
    [TestCase(typeof(VerifySourceFileData), 2)]
    [TestCase(typeof(VerifySuiteCaseData), 4)]
    [TestCase(typeof(VerifySuiteData), 1)]
    [TestCase(typeof(VerifyTaskData), 9)]
    [TestCase(typeof(VerifyTaskBatchData), 5)]
    [TestCase(typeof(VerifyCaseResultData), 5)]
    [TestCase(typeof(VerifyResultData), 12)]
    [TestCase(typeof(VerifyResultBatchData), 3)]
    [TestCase(typeof(VerifyOptionsData), 5)]
    [TestCase(typeof(VerifyRuntimeInfoData), 3)]
    [TestCase(typeof(VerifyRuntimeDiagnosticsData), 4)]
    [TestCase(typeof(VerifyPreflightData), 7)]
    [TestCase(typeof(VerifyBatchDiagnosticsData), 11)]
    [TestCase(typeof(VerifyTaskPackageData), 8)]
    [TestCase(typeof(VerifyResultPackageData), 6)]
    public void WireLayoutIsPinnedTest(Type type, int expectedMemberCount)
    {
        var ordered = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, Order: property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order))
            .Where(member => member.Order != null)
            .OrderBy(member => member.Order)
            .ToList();

        Assert.That(ordered, Has.Count.EqualTo(expectedMemberCount),
            $"{type.Name}: MemoryPack member count changed — this is a wire-layout change. Append-only!");

        for (var i = 0; i < ordered.Count; i++)
        {
            Assert.That(ordered[i].Order, Is.EqualTo(i),
                $"{type.Name}.{ordered[i].Name}: MemoryPackOrder must be contiguous from 0.");
        }
    }

    /// <summary>
    /// The members 0.4.0 appended, by name and order: a ticket on the package's reference, on its
    /// result's reference and inside the result; the controller version and the index digest on
    /// the package; the runtime pin on a batch; a fault code on a result.
    /// </summary>
    [TestCase(typeof(VerifyTaskPackageData), "Ticket", 5)]
    [TestCase(typeof(VerifyTaskPackageData), "RequiredControllerVersion", 6)]
    [TestCase(typeof(VerifyTaskPackageData), "IndexDigest", 7)]
    [TestCase(typeof(VerifyResultPackageData), "Ticket", 5)]
    [TestCase(typeof(VerifyResultBatchData), "Ticket", 2)]
    [TestCase(typeof(VerifyTaskBatchData), "RuntimeSha256", 4)]
    [TestCase(typeof(VerifyResultData), "Fault", 11)]
    public void AppendedMembersKeepTheirPlaceTest(Type type, string member, int expectedOrder)
    {
        var order = type.GetProperty(member)?.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order;

        Assert.That(order, Is.EqualTo(expectedOrder), $"{type.Name}.{member} moved: the wire layout is append-only");
    }

    [Test]
    public void TheVerdictsKeepTheirNumbersTest()
    {
        // A verdict travels as its number: a new one is appended, none is renumbered.
        Assert.Multiple(() =>
        {
            Assert.That((int)VerifyVerdict.Pass, Is.EqualTo(0));
            Assert.That((int)VerifyVerdict.Fail, Is.EqualTo(1));
            Assert.That((int)VerifyVerdict.CompileError, Is.EqualTo(2));
            Assert.That((int)VerifyVerdict.RuntimeError, Is.EqualTo(3));
            Assert.That((int)VerifyVerdict.Timeout, Is.EqualTo(4));
            Assert.That((int)VerifyVerdict.MemoryExceeded, Is.EqualTo(5));
            Assert.That((int)VerifyVerdict.OutputExceeded, Is.EqualTo(6));
            Assert.That((int)VerifyVerdict.RuntimeUnavailable, Is.EqualTo(7));
            Assert.That((int)VerifyVerdict.NoResult, Is.EqualTo(8));
            Assert.That((int)VerifyVerdict.Disputed, Is.EqualTo(9));
            Assert.That(Enum.GetValues<VerifyVerdict>(), Has.Length.EqualTo(10));
        });
    }

    [Test]
    public void AnImageOfTheEarlierLayoutStillReadsTest()
    {
        // Append-only in practice: what a 0.3.0 writer produced for a result reference (five
        // members) is read by this layout, the appended members at their defaults.
        var earlier = new VerifyResultPackageData { ResultsBlobId = Guid.NewGuid(), Sha256 = "ab", SizeBytes = 3, TaskCount = 4, BatchCount = 5, Ticket = "t" };
        var image = MemoryPackSerializer.Serialize(earlier);

        // The member count is the image's first byte; a five-member image ends before the ticket.
        var withoutTicket = MemoryPackSerializer.Serialize(new VerifyResultPackageData { ResultsBlobId = earlier.ResultsBlobId, Sha256 = "ab", SizeBytes = 3, TaskCount = 4, BatchCount = 5 });
        Assert.That(image[0], Is.EqualTo(6), "six members are written");
        Assert.That(withoutTicket[0], Is.EqualTo(6));

        var truncated = withoutTicket[..^4].ToArray();
        truncated[0] = 5;
        var read = MemoryPackSerializer.Deserialize<VerifyResultPackageData>(truncated);

        Assert.That(read, Is.Not.Null);
        Assert.That(read!.TaskCount, Is.EqualTo(4));
        Assert.That(read.Ticket, Is.Null.Or.Empty, "a member the image does not have is left unset: the code reads it as no ticket");
    }

    [Test]
    public void RoundTripPreservesEveryFieldTest()
    {
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = "python-3.14.6",
            DefaultLimits = new VerifyLimitsData
                { FuelBudget = 1, MemoryBytes = 2, WallTimeMs = 3, StdoutLimitBytes = 4, StderrLimitBytes = 5 },
            Tasks =
            [
                new VerifyTaskData
                {
                    TaskIndex = 7,
                    RuntimeId = "python-3.14.6",
                    Sources = [new VerifySourceFileData { Name = "main.py", Content = "print(1)" }],
                    EntryPoint = "main.py",
                    Args = ["--x"],
                    Stdin = "in",
                    RandomSeed = 42,
                    Suite = new VerifySuiteData
                    {
                        Cases = [new VerifySuiteCaseData { Stdin = "s", Args = ["a"], ExpectedStdout = "1\n", ExpectedExitCode = 0 }]
                    },
                    Limits = new VerifyLimitsData { WallTimeMs = 100 }
                }
            ],
            MaxDegree = 3,
            RuntimeSha256 = "cd71"
        };

        var clone = MemoryPackSerializer.Deserialize<VerifyTaskBatchData>(MemoryPackSerializer.Serialize(batch));

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Is(batch), Is.True, "wire round-trip lost a field");
    }
}
