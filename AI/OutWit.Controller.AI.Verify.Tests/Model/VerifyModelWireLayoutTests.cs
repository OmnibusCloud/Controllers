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
            typeof(VerifyOptionsData)
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
    [TestCase(typeof(VerifyTaskBatchData), 3)]
    [TestCase(typeof(VerifyCaseResultData), 5)]
    [TestCase(typeof(VerifyResultData), 11)]
    [TestCase(typeof(VerifyResultBatchData), 1)]
    [TestCase(typeof(VerifyOptionsData), 4)]
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
            ]
        };

        var clone = MemoryPackSerializer.Deserialize<VerifyTaskBatchData>(MemoryPackSerializer.Serialize(batch));

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Is(batch), Is.True, "wire round-trip lost a field");
    }
}
