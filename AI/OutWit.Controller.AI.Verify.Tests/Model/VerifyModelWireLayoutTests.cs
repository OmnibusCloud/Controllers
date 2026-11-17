using System.Collections;
using System.Reflection;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tests.Model;

/// <summary>
/// Freezes the wire layout of every MemoryPack DTO (the repo's append-only wire discipline):
/// for each type the members in their order, by name and type, and the value each has when an
/// image does not carry it. A failing case means a member was inserted, removed, reordered,
/// retyped or given another default: a wire break for the deployed fleet. New members go at
/// the END with the next order value, and are added to the pinned list here.
/// <para>
/// The guard used to pin a member count for each type and that the orders ran from 0: two
/// members that changed places, or a member that changed its type, passed it (audit finding
/// V:M-7). It also holds the bytes themselves: an image of every travelling type, written by
/// the layout as it was released, must still be what this build writes and reads.
/// </para>
/// </summary>
[TestFixture]
public class VerifyModelWireLayoutTests
{
    #region Constants

    // order, name, type = the value of a new instance (what a member an image lacks is read as).
    private static readonly Dictionary<Type, string[]> LAYOUT = new()
    {
        [typeof(VerifyBatchDiagnosticsData)] =
        [
            "0 NodeId Guid = empty",
            "1 NodeName string = \"\"",
            "2 RuntimeTarget string = \"\"",
            "3 WasmtimeVersion string = \"\"",
            "4 NativeLibraryPath string = \"\"",
            "5 RuntimeId string = \"\"",
            "6 RuntimeSha256 string = \"\"",
            "7 Degree int = 0",
            "8 TaskCount int = 0",
            "9 WallMs int = 0",
            "10 ControllerVersion string = \"\""
        ],
        [typeof(VerifyCaseResultData)] =
        [
            "0 CaseIndex int = 0",
            "1 Passed bool = false",
            "2 Verdict VerifyVerdict = Pass",
            "3 ExitCode int = 0",
            "4 ActualStdout string = \"\""
        ],
        [typeof(VerifyLimitsData)] =
        [
            "0 FuelBudget long = 0",
            "1 MemoryBytes long = 0",
            "2 WallTimeMs int = 0",
            "3 StdoutLimitBytes int = 0",
            "4 StderrLimitBytes int = 0"
        ],
        [typeof(VerifyOptionsData)] =
        [
            "0 DefaultLimits VerifyLimitsData? = null",
            "1 BatchSize int = 0",
            "2 SamplingRate double = 0",
            "3 ReportFormat VerifyReportFormat = Jsonl",
            "4 MaxDegree int = 0"
        ],
        [typeof(VerifyPreflightData)] =
        [
            "0 WellFormed bool = false",
            "1 TaskCount int = 0",
            "2 BatchCount int = 0",
            "3 RuntimeIds List<string> = []",
            "4 UnknownRuntimeIds List<string> = []",
            "5 EstimatedInputBytes long = 0",
            "6 Messages List<string> = []"
        ],
        [typeof(VerifyResultBatchData)] =
        [
            "0 Results List<VerifyResultData> = []",
            "1 Diagnostics VerifyBatchDiagnosticsData? = null",
            "2 Ticket string = \"\""
        ],
        [typeof(VerifyResultData)] =
        [
            "0 TaskIndex int = 0",
            "1 Verdict VerifyVerdict = Pass",
            "2 ExitCode int = 0",
            "3 Stdout string = \"\"",
            "4 StdoutTruncated bool = false",
            "5 Stderr string = \"\"",
            "6 StderrTruncated bool = false",
            "7 CaseResults List<VerifyCaseResultData> = []",
            "8 FuelConsumed long = 0",
            "9 PeakMemoryBytes long = 0",
            "10 WallMs int = 0",
            "11 Fault string = \"\""
        ],
        [typeof(VerifyResultPackageData)] =
        [
            "0 ResultsBlobId Guid = empty",
            "1 Sha256 string = \"\"",
            "2 SizeBytes long = 0",
            "3 TaskCount int = 0",
            "4 BatchCount int = 0",
            "5 Ticket string = \"\""
        ],
        [typeof(VerifyRuntimeDiagnosticsData)] =
        [
            "0 RuntimeTarget string = \"\"",
            "1 WasmtimeVersion string = \"\"",
            "2 RuntimesRootFound bool = false",
            "3 Runtimes List<VerifyRuntimeInfoData> = []"
        ],
        [typeof(VerifyRuntimeInfoData)] =
        [
            "0 RuntimeId string = \"\"",
            "1 Sha256 string = \"\"",
            "2 Available bool = false"
        ],
        [typeof(VerifySourceFileData)] =
        [
            "0 Name string = \"\"",
            "1 Content string = \"\""
        ],
        [typeof(VerifySuiteCaseData)] =
        [
            "0 Stdin string? = null",
            "1 Args List<string> = []",
            "2 ExpectedStdout string? = null",
            "3 ExpectedExitCode int = 0"
        ],
        [typeof(VerifySuiteData)] =
        [
            "0 Cases List<VerifySuiteCaseData> = []"
        ],
        [typeof(VerifyTaskBatchData)] =
        [
            "0 RuntimeId string = \"\"",
            "1 DefaultLimits VerifyLimitsData? = null",
            "2 Tasks List<VerifyTaskData> = []",
            "3 MaxDegree int = 0",
            "4 RuntimeSha256 string = \"\""
        ],
        [typeof(VerifyTaskData)] =
        [
            "0 TaskIndex int = 0",
            "1 RuntimeId string = \"\"",
            "2 Sources List<VerifySourceFileData> = []",
            "3 EntryPoint string = \"\"",
            "4 Args List<string> = []",
            "5 Stdin string? = null",
            "6 RandomSeed long = 0",
            "7 Suite VerifySuiteData? = null",
            "8 Limits VerifyLimitsData? = null"
        ],
        [typeof(VerifyTaskPackageData)] =
        [
            "0 TasksBlobId Guid = empty",
            "1 Sha256 string = \"\"",
            "2 SizeBytes long = 0",
            "3 TaskCount int = 0",
            "4 BatchCount int = 0",
            "5 Ticket string = \"\"",
            "6 RequiredControllerVersion string = \"\"",
            "7 IndexDigest string = \"\""
        ]
    };

    private static readonly Dictionary<Type, string> SIMPLE_NAMES = new()
    {
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(bool)] = "bool",
        [typeof(double)] = "double",
        [typeof(string)] = "string"
    };

    private static readonly Guid GOLDEN_BLOB = new("00112233-4455-6677-8899-aabbccddeeff");

    // The images of Verify.Model 0.4.0, the layout on the fleet (lower-case hex).
    private const string GOLDEN_TASK_BATCH = "05f2ffffff0d000000707974686f6e2d332e31342e360501000000000000000200000000000000030000000400000005000000010000000907000000f2ffffff0d000000707974686f6e2d332e31342e360100000002f8ffffff070000006d61696e2e7079f7ffffff080000007072696e74283129f8ffffff070000006d61696e2e707901000000fcffffff030000002d2d78fdffffff02000000696e2a00000000000000010100000004feffffff010000007301000000feffffff0100000061fdffffff02000000310a03000000050000000000000000000000000000000064000000000000000000000003000000fbffffff0400000063643731";
    private const string GOLDEN_RESULT_BATCH = "03010000000c0700000006000000fffffffffcffffff030000006f757401fcffffff0300000065727201010000000501000000010100000002000000feffffff010000006f621e4b0700000000000080020000000034000000f5ffffff0a00000077616c6c5f636c6f636b0b33221100554477668899aabbccddeefffeffffff010000006ef4ffffff0b00000077696e646f77732d783634f9ffffff0600000034382e302e38feffffff0100000070f2ffffff0d000000707974686f6e2d332e31342e36fbffffff040000006364373110000000010000003c000000f9ffffff06000000302e31332e30dfffffff200000003031323334353637383961626364656630313233343536373839616263646566";
    private const string GOLDEN_TASK_PACKAGE = "0833221100554477668899aabbccddeefffdffffff020000003561cb04fb711f010000c701000008000000dfffffff200000003031323334353637383961626364656630313233343536373839616263646566f9ffffff06000000302e31332e30fdffffff020000006431";
    private const string GOLDEN_RESULT_PACKAGE = "0633221100554477668899aabbccddeefffdffffff020000006135ea16b04c02000000c701000008000000dfffffff200000006665646362613938373635343332313066656463626139383736353433323130";
    private const string GOLDEN_OPTIONS = "05050b000000000000000c000000000000000d0000000e0000000f00000040000000000000000000d03f0100000007000000";
    private const string GOLDEN_PREFLIGHT = "0701030000000200000002000000f2ffffff0d000000707974686f6e2d332e31342e36fbffffff040000007761736901000000f5ffffff0a000000636f626f6c2d31393539001000000000000001000000e6ffffff190000006c696e6520333a206d697373696e67202772756e74696d6527";
    private const string GOLDEN_RUNTIME_DIAGNOSTICS = "04f6ffffff090000006c696e75782d783634f9ffffff0600000034382e302e38010100000003f6ffffff090000006c75612d352e342e38fdffffff02000000356201";

    #endregion

    #region Layout Tests

    [Test]
    public void PinListCoversAllMemoryPackableDtosTest()
    {
        // The append-only discipline is only as good as this list: a new
        // [MemoryPackable] DTO must be added to the pinned layout above.
        var actual = typeof(VerifyTaskData).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes(inherit: false)
                .Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute"))
            .ToArray();

        Assert.That(actual, Is.EquivalentTo(LAYOUT.Keys),
            "a MemoryPackable DTO exists that is not wire-layout-pinned (or a pinned one was removed)");
    }

    [TestCaseSource(nameof(PinnedTypes))]
    public void M7TheWireLayoutIsPinnedByMemberOrderTypeAndDefaultTest(Type type)
    {
        var fresh = Activator.CreateInstance(type)!;
        var nullability = new NullabilityInfoContext();

        var actual = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Order: property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order))
            .Where(member => member.Order != null)
            .OrderBy(member => member.Order)
            .Select(member => $"{member.Order} {member.Property.Name} {TypeName(member.Property, nullability)} = {Shown(member.Property.GetValue(fresh))}")
            .ToArray();

        Assert.That(actual, Is.EqualTo(LAYOUT[type]),
            $"{type.Name}: a member was inserted, removed, reordered, retyped or given another default. The wire layout is append-only: a new member goes at the end with the next order.");

        // Every public property with a setter is on the wire: one left without an order would be
        // lost between a server and a node without a word.
        var unordered = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite && property.GetCustomAttribute<MemoryPackOrderAttribute>() == null && property.GetCustomAttribute<MemoryPackIgnoreAttribute>() == null)
            .Select(property => property.Name);

        Assert.That(unordered, Is.Empty, $"{type.Name}: a settable member has no MemoryPackOrder");
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
            Assert.That((int)VerifyReportFormat.Jsonl, Is.EqualTo(0));
            Assert.That((int)VerifyReportFormat.Json, Is.EqualTo(1));
        });
    }

    #endregion

    #region Image Tests

    [TestCaseSource(nameof(GoldenImages))]
    public void M7AnImageIsTheBytesTheReleasedLayoutWritesTest(string name, object value, string golden)
    {
        // The bytes themselves, with every member set to a value of its own: the layout above
        // says what the members are, this says what they are on the wire. Written by the layout
        // of Verify.Model 0.4.0, the one on the fleet.
        var image = Convert.ToHexStringLower(MemoryPackSerializer.Serialize(value.GetType(), value));

        Assert.That(image, Is.EqualTo(golden), $"{name}: this build writes other bytes than the released layout. Actual: {image}");

        var read = MemoryPackSerializer.Deserialize(value.GetType(), Convert.FromHexString(golden)) as OutWit.Common.Abstract.ModelBase;
        Assert.That(read?.Is((OutWit.Common.Abstract.ModelBase)value), Is.True, $"{name}: the released image does not read back as the value it was written from");
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
        var batch = GoldenTaskBatch();

        var clone = MemoryPackSerializer.Deserialize<VerifyTaskBatchData>(MemoryPackSerializer.Serialize(batch));

        Assert.That(clone, Is.Not.Null);
        Assert.That(clone!.Is(batch), Is.True, "wire round-trip lost a field");
    }

    #endregion

    #region Tools

    private static IEnumerable<Type> PinnedTypes()
    {
        return LAYOUT.Keys;
    }

    private static IEnumerable<TestCaseData> GoldenImages()
    {
        yield return new TestCaseData("a task batch", GoldenTaskBatch(), GOLDEN_TASK_BATCH).SetArgDisplayNames("VerifyTaskBatchData");
        yield return new TestCaseData("a result batch", GoldenResultBatch(), GOLDEN_RESULT_BATCH).SetArgDisplayNames("VerifyResultBatchData");
        yield return new TestCaseData("a task package reference", new VerifyTaskPackageData
        {
            TasksBlobId = GOLDEN_BLOB, Sha256 = "5a", SizeBytes = 1234567890123, TaskCount = 455, BatchCount = 8,
            Ticket = "0123456789abcdef0123456789abcdef", RequiredControllerVersion = "0.13.0", IndexDigest = "d1"
        }, GOLDEN_TASK_PACKAGE).SetArgDisplayNames("VerifyTaskPackageData");
        yield return new TestCaseData("a result package reference", new VerifyResultPackageData
        {
            ResultsBlobId = GOLDEN_BLOB, Sha256 = "a5", SizeBytes = 9876543210, TaskCount = 455, BatchCount = 8, Ticket = "fedcba9876543210fedcba9876543210"
        }, GOLDEN_RESULT_PACKAGE).SetArgDisplayNames("VerifyResultPackageData");
        yield return new TestCaseData("the options", new VerifyOptionsData
        {
            DefaultLimits = new VerifyLimitsData { FuelBudget = 11, MemoryBytes = 12, WallTimeMs = 13, StdoutLimitBytes = 14, StderrLimitBytes = 15 },
            BatchSize = 64, SamplingRate = 0.25, ReportFormat = VerifyReportFormat.Json, MaxDegree = 7
        }, GOLDEN_OPTIONS).SetArgDisplayNames("VerifyOptionsData");
        yield return new TestCaseData("a preflight's answer", new VerifyPreflightData
        {
            WellFormed = true, TaskCount = 3, BatchCount = 2, RuntimeIds = ["python-3.14.6", "wasi"], UnknownRuntimeIds = ["cobol-1959"],
            EstimatedInputBytes = 4096, Messages = ["line 3: missing 'runtime'"]
        }, GOLDEN_PREFLIGHT).SetArgDisplayNames("VerifyPreflightData");
        yield return new TestCaseData("the runtime diagnostics", new VerifyRuntimeDiagnosticsData
        {
            RuntimeTarget = "linux-x64", WasmtimeVersion = "48.0.8", RuntimesRootFound = true,
            Runtimes = [new VerifyRuntimeInfoData { RuntimeId = "lua-5.4.8", Sha256 = "5b", Available = true }]
        }, GOLDEN_RUNTIME_DIAGNOSTICS).SetArgDisplayNames("VerifyRuntimeDiagnosticsData");
    }

    private static VerifyTaskBatchData GoldenTaskBatch()
    {
        return new VerifyTaskBatchData
        {
            RuntimeId = "python-3.14.6",
            DefaultLimits = new VerifyLimitsData { FuelBudget = 1, MemoryBytes = 2, WallTimeMs = 3, StdoutLimitBytes = 4, StderrLimitBytes = 5 },
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
                        Cases = [new VerifySuiteCaseData { Stdin = "s", Args = ["a"], ExpectedStdout = "1\n", ExpectedExitCode = 3 }]
                    },
                    Limits = new VerifyLimitsData { WallTimeMs = 100 }
                }
            ],
            MaxDegree = 3,
            RuntimeSha256 = "cd71"
        };
    }

    private static VerifyResultBatchData GoldenResultBatch()
    {
        return new VerifyResultBatchData
        {
            Results =
            [
                new VerifyResultData
                {
                    TaskIndex = 7,
                    Verdict = VerifyVerdict.OutputExceeded,
                    ExitCode = -1,
                    Stdout = "out",
                    StdoutTruncated = true,
                    Stderr = "err",
                    StderrTruncated = true,
                    CaseResults = [new VerifyCaseResultData { CaseIndex = 1, Passed = true, Verdict = VerifyVerdict.Fail, ExitCode = 2, ActualStdout = "o" }],
                    FuelConsumed = 122363490,
                    PeakMemoryBytes = 41943040,
                    WallMs = 52,
                    Fault = "wall_clock"
                }
            ],
            Diagnostics = new VerifyBatchDiagnosticsData
            {
                NodeId = GOLDEN_BLOB, NodeName = "n", RuntimeTarget = "windows-x64", WasmtimeVersion = "48.0.8", NativeLibraryPath = "p",
                RuntimeId = "python-3.14.6", RuntimeSha256 = "cd71", Degree = 16, TaskCount = 1, WallMs = 60, ControllerVersion = "0.13.0"
            },
            Ticket = "0123456789abcdef0123456789abcdef"
        };
    }

    private static string TypeName(PropertyInfo property, NullabilityInfoContext nullability)
    {
        var name = TypeName(property.PropertyType);
        return !property.PropertyType.IsValueType && nullability.Create(property).ReadState == NullabilityState.Nullable ? name + "?" : name;
    }

    private static string TypeName(Type type)
    {
        if (SIMPLE_NAMES.TryGetValue(type, out var simple))
            return simple;

        return type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>"
            : type.Name;
    }

    private static string Shown(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            bool flag => flag ? "true" : "false",
            Guid id => id == Guid.Empty ? "empty" : id.ToString(),
            IEnumerable items => items.Cast<object>().Any() ? "[...]" : "[]",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
        };
    }

    #endregion
}
