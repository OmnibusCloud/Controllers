using System.Reflection;
using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// Holds the wire layout of every MemoryPack type of the Model, member by member: the order, the
/// name, the type and the default of each. A failing case means a member was inserted, appended,
/// reordered, renamed, retyped, removed, or given another default: a wire change for a deployed
/// fleet and for every application built against the package. No such change is compatible on its
/// own, appending included: MemoryPack in its default mode reads a payload of fewer members than
/// its reader knows (the missing ones arrive as their type's default: null for a text or a list,
/// never the property's initializer) and throws on a payload of more. A published layout changes
/// only behind a version fence, so that no reader of the old layout is ever handed the new one;
/// and a member a sender may not have starts at its type's default and, when it is a reference,
/// is declared nullable.
/// </summary>
[TestFixture]
public sealed class SmtModelWireLayoutTests
{
    #region Constants

    /// <summary>Set to a file path to have the current layout written there (to move the baseline on purpose).</summary>
    private const string WRITE_BASELINE_VARIABLE = "WITAI_SMT_WRITE_WIRE_BASELINE";

    /// <summary>
    /// How many members each type has in its first layout: every sender has those, so they may
    /// start at anything (the sampling rate of a run's options starts at the default rate: 0 is a
    /// rate of its own, so "not set" cannot be 0 there). A member past them is one a sender may
    /// not have. The counts are frozen at the layout of 1.0.0, the package's first release: from
    /// that release on, a member past them comes only behind a version fence (a new controller and
    /// model version, whose payloads no reader of the old layout is ever handed), and it starts at
    /// its type's default.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> FIRST_LAYOUT_MEMBERS = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [nameof(SmtCountData)] = 2,
        [nameof(SmtOptionsData)] = 8,
        [nameof(SmtPreflightData)] = 29,
        [nameof(SmtProblemData)] = 11,
        [nameof(SmtRefusalData)] = 6,
        [nameof(SmtResultData)] = 26,
        [nameof(SmtResultPackageData)] = 7,
        [nameof(SmtRuntimeDiagnosticsData)] = 21,
        [nameof(SmtTaskPackageData)] = 8
    };

    #endregion

    #region Layout Tests

    [Test]
    public void TheWireLayoutIsTheBaselinesMemberByMemberTest()
    {
        var root = SolutionRoot();
        var current = SmtModelWireLayout.Describe(ModelVersion(root));

        if (Environment.GetEnvironmentVariable(WRITE_BASELINE_VARIABLE) is { Length: > 0 } target)
            File.WriteAllText(target, current + "\n");

        using var baseline = JsonDocument.Parse(File.ReadAllBytes(BaselinePath(root)));
        var expected = baseline.RootElement.GetProperty("types").EnumerateObject()
            .ToDictionary(type => type.Name, type => type.Value.EnumerateArray().Select(member => member.GetString()!).ToList(), StringComparer.Ordinal);
        var actual = SmtModelWireLayout.Types()
            .ToDictionary(type => type.Name, type => SmtModelWireLayout.Members(type).Select(member => member.ToString()).ToList(), StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys), "a MemoryPack type was added to the Model or removed from it: the baseline lists every one");
            foreach (var (type, members) in actual)
            {
                if (expected.TryGetValue(type, out var pinned))
                    Assert.That(members, Is.EqualTo(pinned), $"{type}: a member's order, name, type or default differs from the baseline, or a member was added. A published layout changes only behind a version fence (appending is no compatible change: an older reader throws on the newer payload); then update wire-layout.baseline.json in the same commit. The current layout:\n{current}");
            }
        });
    }

    [Test]
    public void MemberOrdersAreContiguousFromZeroTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                var members = SmtModelWireLayout.Members(type);
                Assert.That(members, Is.Not.Empty, $"{type.Name}: a wire type without members");
                Assert.That(members.Select(member => member.Order), Is.EqualTo(Enumerable.Range(0, members.Count)), $"{type.Name}: orders must be contiguous from 0");
            }
        });
    }

    [Test]
    public void EveryPublicPropertyOfAWireTypeIsOnTheWireTest()
    {
        // A property without an order is not sent: a value that looks whole on one side and
        // arrives without a part of it on the other.
        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                var settable = type.GetProperties().Where(property => property.DeclaringType == type && property.CanWrite).Select(property => property.Name);
                Assert.That(SmtModelWireLayout.Members(type).Select(member => member.Name), Is.EquivalentTo(settable), type.Name);
            }
        });
    }

    [Test]
    public void BaselineMatchesTheModelVersionTest()
    {
        var root = SolutionRoot();
        using var baseline = JsonDocument.Parse(File.ReadAllBytes(BaselinePath(root)));

        Assert.That(ModelVersion(root), Is.EqualTo(baseline.RootElement.GetProperty("modelVersion").GetString()), "the Model csproj version and the baseline's modelVersion must agree");
    }

    [Test]
    public void AMemberASenderMayNotHaveStartsAtItsTypesDefaultTest()
    {
        // What a reader gets for a member its sender does not have is the member's type's
        // default: 0 or false for a value, null for a text, a list or a model, never the
        // property's initializer. A member past its type's first layout starts there on a new
        // instance too, so that it reads the same from an older sender as from a new instance,
        // and a reference member is declared nullable, so that its readers handle the null.
        var nullability = new NullabilityInfoContext();

        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                if (!FIRST_LAYOUT_MEMBERS.TryGetValue(type.Name, out var first))
                {
                    Assert.Fail($"{type.Name}: a wire type without the member count of its first layout");
                    continue;
                }

                Assert.That(SmtModelWireLayout.Members(type), Has.Count.GreaterThanOrEqualTo(first), $"{type.Name}: a member of its first layout was removed");

                var instance = Activator.CreateInstance(type)!;
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetCustomAttribute<MemoryPackOrderAttribute>() is not { } order || order.Order < first)
                        continue;

                    var value = property.GetValue(instance);
                    if (property.PropertyType.IsValueType)
                    {
                        Assert.That(value, Is.EqualTo(Activator.CreateInstance(property.PropertyType)), $"{type.Name}.{property.Name}: a member a sender may not have starts at its type's default");
                        continue;
                    }

                    Assert.That(value, Is.Null, $"{type.Name}.{property.Name}: a member a sender may not have arrives null from that sender, so it starts null");
                    Assert.That(nullability.Create(property).ReadState, Is.EqualTo(NullabilityState.Nullable), $"{type.Name}.{property.Name}: a reference member a sender may not have is declared nullable");
                }
            }
        });
    }

    #endregion

    #region Evolution Tests

    [Test]
    public void AnOlderSendersPayloadLeavesTheMemberItLacksAtItsTypesDefaultTest()
    {
        // The payload of a sender whose row had no digest yet: the same bytes, one member fewer
        // in the header and without the last member's. MemoryPack reads it, and the member the
        // sender did not have arrives null, not as the property's initializer: the text a row
        // is declared to always have.
        var bytes = MemoryPackSerializer.Serialize(new SmtResultData { Id = "cage-1", Transcript = "> (check-sat)\nsat\n", Digest = "" });
        var members = SmtModelWireLayout.Members(typeof(SmtResultData)).Count;

        Assert.That(bytes[0], Is.EqualTo(members), "a payload starts with its count of members");
        Assert.That(bytes[^4..], Is.EqualTo(new byte[4]), "and here ends with the empty text of the last member");

        var older = bytes[..^4];
        older[0] = (byte)(members - 1);
        var back = MemoryPackSerializer.Deserialize<SmtResultData>(older)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Digest, Is.Null, "the member the sender did not have");
            Assert.That(back.Id, Is.EqualTo("cage-1"));
            Assert.That(back.Transcript, Is.EqualTo("> (check-sat)\nsat\n"));
        });
    }

    [Test]
    public void ANewerSendersPayloadIsRefusedTest()
    {
        // The payload of a sender whose row has one member more (an empty text appended): a
        // reader of this layout throws instead of reading the members it knows. Appending a
        // member to a published type is no compatible change.
        var bytes = MemoryPackSerializer.Serialize(new SmtResultData());
        var members = SmtModelWireLayout.Members(typeof(SmtResultData)).Count;

        var newer = bytes.Concat(new byte[4]).ToArray();
        newer[0] = (byte)(members + 1);

        Assert.Throws<MemoryPackSerializationException>(() => MemoryPackSerializer.Deserialize<SmtResultData>(newer));
    }

    #endregion

    #region Round Trips

    [Test]
    public void RefusalRoundTripsThroughMemoryPackAndComparesByValueTest()
    {
        var refusal = new SmtRefusalData { File = "qf_lia/0841.smt2", Line = 12, Command = "push", Code = "command_not_allowed", Reason = "incremental scripts, unsat cores and proofs are not supported", Row = 841 };

        var back = MemoryPackSerializer.Deserialize<SmtRefusalData>(MemoryPackSerializer.Serialize(refusal))!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(refusal), Is.True);
            Assert.That(back.File, Is.EqualTo("qf_lia/0841.smt2"));
            Assert.That(back.Line, Is.EqualTo(12));
            Assert.That(back.Command, Is.EqualTo("push"));
            Assert.That(back.Code, Is.EqualTo("command_not_allowed"));
            Assert.That(back.Reason, Is.EqualTo(refusal.Reason));
            Assert.That(back.Row, Is.EqualTo(841));
        });
    }

    [Test]
    public void RefusalComparesEveryMemberAndClonesDeepTest()
    {
        var refusal = new SmtRefusalData { File = "a.smt2", Line = 3, Command = "echo", Code = "command_not_allowed", Reason = "r", Row = 7 };
        var clone = refusal.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.SameAs(refusal));
            Assert.That(clone.Is(refusal), Is.True);
            Assert.That(clone.Row, Is.EqualTo(7));
            Assert.That(new SmtRefusalData().Is(new SmtRefusalData()), Is.True);
            Assert.That(Changed(refusal, other => other.Row = 8).Is(refusal), Is.False);

            Assert.That(Changed(refusal, other => other.File = "b.smt2").Is(refusal), Is.False);
            Assert.That(Changed(refusal, other => other.Line = 4).Is(refusal), Is.False);
            Assert.That(Changed(refusal, other => other.Command = "eval").Is(refusal), Is.False);
            Assert.That(Changed(refusal, other => other.Code = "syntax_error").Is(refusal), Is.False);
            Assert.That(Changed(refusal, other => other.Reason = "s").Is(refusal), Is.False);
        });
    }

    [Test]
    public void RefusalSaysWhereAndWhatButNotTheReasonTest()
    {
        var text = new SmtRefusalData { File = "a.smt2", Line = 3, Command = "echo", Code = "command_not_allowed", Reason = "the reason" }.ToString();

        Assert.That(text, Does.Contain("a.smt2").And.Contain("3").And.Contain("echo").And.Contain("command_not_allowed"));
        Assert.That(text, Does.Not.Contain("the reason"));
    }

    #endregion

    #region Tools

    private static SmtRefusalData Changed(SmtRefusalData refusal, Action<SmtRefusalData> change)
    {
        var other = refusal.Clone();
        change(other);
        return other;
    }

    private static string BaselinePath(string root)
    {
        return Path.Combine(root, "AI", "OutWit.Controller.AI.Smt.Tests", "Model", "wire-layout.baseline.json");
    }

    private static string ModelVersion(string root)
    {
        var csproj = File.ReadAllText(Path.Combine(root, "AI", "OutWit.Controller.AI.Smt.Model", "OutWit.Controller.AI.Smt.Model.csproj"));
        return System.Text.RegularExpressions.Regex.Match(csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;
    }

    private static string SolutionRoot()
    {
        var directory = TestContext.CurrentContext.TestDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "OutWit.slnx")))
                return directory;

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("The solution root (OutWit.slnx) was not found above the test folder.");
    }

    #endregion
}
