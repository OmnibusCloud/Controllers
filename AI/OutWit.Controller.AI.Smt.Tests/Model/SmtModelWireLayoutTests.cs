using System.Text.Json;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// Holds the wire layout of every MemoryPack type of the Model, member by member: the order, the
/// name, the type and the default of each. A failing case means a member was inserted, reordered,
/// renamed, retyped, removed, or given another default: a wire break for a deployed fleet and for
/// every application built against the package. New members go at the END with the next order
/// value, and start at their type's default: a member the sender does not have arrives as its
/// type's default, not as the property's initializer.
/// </summary>
[TestFixture]
public sealed class SmtModelWireLayoutTests
{
    #region Constants

    /// <summary>Set to a file path to have the current layout written there (to move the baseline on purpose).</summary>
    private const string WRITE_BASELINE_VARIABLE = "WITAI_SMT_WRITE_WIRE_BASELINE";

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
                    Assert.That(members, Is.EqualTo(pinned), $"{type}: a member's order, name, type or default differs from the baseline. Append only; then update wire-layout.baseline.json in the same commit. The current layout:\n{current}");
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
    public void AMembersInitializerIsItsTypesDefaultTest()
    {
        // What a reader gets for a member its sender does not have is the type's default. A
        // value member with another initializer would read differently from an older sender
        // than from a new instance.
        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                var other = SmtModelWireLayout.Members(type)
                    .Where(member => member.Type is "Boolean" or "Int32" or "Int64" or "Double" && member.Default is not ("false" or "0"))
                    .Select(member => member.Name);

                Assert.That(other, Is.Empty, $"{type.Name}: a value member starts at something else than its type's default");
            }
        });
    }

    #endregion

    #region Round Trips

    [Test]
    public void RefusalRoundTripsThroughMemoryPackAndComparesByValueTest()
    {
        var refusal = new SmtRefusalData { File = "qf_lia/0841.smt2", Line = 12, Command = "push", Code = "command_not_allowed", Reason = "incremental scripts, unsat cores and proofs are not supported" };

        var back = MemoryPackSerializer.Deserialize<SmtRefusalData>(MemoryPackSerializer.Serialize(refusal))!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(refusal), Is.True);
            Assert.That(back.File, Is.EqualTo("qf_lia/0841.smt2"));
            Assert.That(back.Line, Is.EqualTo(12));
            Assert.That(back.Command, Is.EqualTo("push"));
            Assert.That(back.Code, Is.EqualTo("command_not_allowed"));
            Assert.That(back.Reason, Is.EqualTo(refusal.Reason));
        });
    }

    [Test]
    public void RefusalComparesEveryMemberAndClonesDeepTest()
    {
        var refusal = new SmtRefusalData { File = "a.smt2", Line = 3, Command = "echo", Code = "command_not_allowed", Reason = "r" };
        var clone = refusal.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone, Is.Not.SameAs(refusal));
            Assert.That(clone.Is(refusal), Is.True);
            Assert.That(new SmtRefusalData().Is(new SmtRefusalData()), Is.True);

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
