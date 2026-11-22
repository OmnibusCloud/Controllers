using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// The command policy as a table: every command name of the standard, the solver's extensions
/// and names nobody has heard of, each with what becomes of it. The table is the tests' own: it
/// does not read the lists of the code it checks.
/// </summary>
[TestFixture]
public class SmtSanitizerPolicyTests
{
    #region Constants

    private const string REASON_INCREMENTAL = "incremental scripts, unsat cores and proofs are not supported";
    private const string REASON_OPTION = "the controller sets every option itself";
    private const string REASON_OTHER = "a problem holds declarations, definitions and assertions; no other command is taken";
    private const string REASON_BARS = "a command's name is written without bars";

    /// <summary>The kept commands, each in a shape the standard gives it.</summary>
    private static readonly Dictionary<string, string> KEPT = new(StringComparer.Ordinal)
    {
        ["set-logic"] = "(set-logic QF_LIA)",
        ["set-info"] = "(set-info :status sat)",
        ["declare-sort"] = "(declare-sort S 0)",
        ["define-sort"] = "(define-sort T () Int)",
        ["declare-const"] = "(declare-const x Int)",
        ["declare-fun"] = "(declare-fun f (Int) Int)",
        ["define-fun"] = "(define-fun f ((x Int)) Int x)",
        ["define-fun-rec"] = "(define-fun-rec f ((x Int)) Int (f x))",
        ["define-funs-rec"] = "(define-funs-rec ((f ((x Int)) Int)) ((f x)))",
        ["declare-datatype"] = "(declare-datatype D ((mk (get Int))))",
        ["declare-datatypes"] = "(declare-datatypes ((D 0)) (((mk (get Int)))))",
        ["assert"] = "(assert true)"
    };

    private static readonly string[] STRIPPED = ["check-sat", "get-model", "get-value", "get-info", "exit"];

    private static readonly string[] INCREMENTAL =
    [
        "push", "pop", "reset", "reset-assertions", "check-sat-assuming", "get-unsat-core", "get-unsat-assumptions",
        "get-assertions", "get-assignment", "get-proof", "get-option"
    ];

    /// <summary>The solver's extensions and <c>echo</c>: refused by name, as every unknown command is.</summary>
    private static readonly string[] OTHER =
    [
        "echo", "include", "eval", "simplify", "apply", "display", "help", "labels", "declare-rel", "rule", "query",
        "declare-var", "maximize", "minimize", "assert-soft", "check-sat-using", "get-objectives", "get-user-tactics",
        "declare-map", "define-const", "dbg-set-next-id", "dbg-lt", "dbg-"
    ];

    /// <summary>Names of no command at all, and near misses of the kept ones.</summary>
    private static readonly string[] INVENTED =
    [
        "frobnicate", "assert!", "Assert", "ASSERT", "assert-not", "asserts", "declare", "declare-consts", "set-logics",
        "check-sat-twice", "get-everything", "x", "+", "true", "let", "forall", "_", "!", "par", "exit!", "set-info-all"
    ];

    #endregion

    #region Table Tests

    [Test]
    public void PolicyNamesEveryCommandOfTheStandardTest()
    {
        var named = KEPT.Keys.Concat(STRIPPED).Concat(INCREMENTAL).Append("set-option").Append("echo").ToList();

        Assert.That(named, Is.Unique);
        Assert.That(named, Is.EquivalentTo(SmtReservedWords.Commands), "a command of the standard has no line in the policy table");
    }

    [TestCaseSource(nameof(KeptNames))]
    public void KeptCommandStaysInTheProblemTest(string name)
    {
        var problem = SmtSanitizerChecks.Accept(KEPT[name]);

        Assert.That(problem.Commands.Select(command => command.Head), Is.EqualTo(new[] { name }));
        Assert.That(problem.Canonical, Is.EqualTo(KEPT[name] + "\n"));
        Assert.That(problem.Stripped, Is.Empty);
        Assert.That(problem.StrippedLines, Is.Empty);
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [TestCaseSource(nameof(StrippedNames))]
    public void StrippedCommandIsCountedAndNotKeptTest(string name)
    {
        var problem = SmtSanitizerChecks.Accept($"(declare-const x Bool)\n(assert x)\n({name})\n\n({name} (x) :all \"text\")\n");

        Assert.That(problem.Commands.Select(command => command.Head), Is.EqualTo(new[] { "declare-const", "assert" }));
        Assert.That(problem.Canonical, Is.EqualTo("(declare-const x Bool)\n(assert x)\n"));
        Assert.That(problem.Stripped, Has.Count.EqualTo(1));
        Assert.That(problem.Stripped[name], Is.EqualTo(2));
        Assert.That(problem.StrippedLines.Select(stripped => (stripped.Line, stripped.Command)), Is.EqualTo(new[] { (3, name), (5, name) }));
        SmtSanitizerChecks.HoldProperties(problem);
    }

    [TestCaseSource(nameof(IncrementalNames))]
    public void IncrementalCommandRefusesTheProblemTest(string name)
    {
        AssertRefusedByName(name, REASON_INCREMENTAL);
    }

    [Test]
    public void SetOptionRefusesTheProblemTest()
    {
        AssertRefusedByName("set-option", REASON_OPTION);
    }

    [TestCaseSource(nameof(OtherNames))]
    public void ExtensionCommandRefusesTheProblemTest(string name)
    {
        AssertRefusedByName(name, REASON_OTHER);
    }

    [TestCaseSource(nameof(InventedNames))]
    public void UnknownCommandRefusesTheProblemTest(string name)
    {
        AssertRefusedByName(name, REASON_OTHER);
    }

    #endregion

    #region Head Tests

    [Test]
    public void CommandNameBetweenBarsRefusesTheProblemTest()
    {
        // Between bars a command name is an ordinary symbol by the standard; a solver that looks
        // the symbol up by its name runs the command. Kept, stripped or refused: none is taken.
        foreach (var name in SmtReservedWords.Commands)
        {
            var text = KEPT.TryGetValue(name, out var kept) ? kept.Replace($"({name}", $"(|{name}|") : $"(|{name}|)";
            var refusal = SmtSanitizerChecks.Refuse($"\n{text}", SmtRefusalCodes.COMMAND_NOT_ALLOWED);

            Assert.That(refusal.Command, Is.EqualTo(name), name);
            Assert.That(refusal.Line, Is.EqualTo(2), name);
            Assert.That(refusal.Reason, Is.EqualTo(REASON_BARS), name);
        }
    }

    [Test]
    public void TheFirstRefusedCommandIsTheOneNamedTest()
    {
        var refusal = SmtSanitizerChecks.Refuse("(declare-const x Int)\n(check-sat)\n(push 1)\n(set-option :a 1)\n(echo \"x\")", SmtRefusalCodes.COMMAND_NOT_ALLOWED);

        Assert.That(refusal.Command, Is.EqualTo("push"));
        Assert.That(refusal.Line, Is.EqualTo(3));
    }

    [Test]
    public void RefusedCommandIsRefusedWhateverItsShapeTest()
    {
        foreach (var text in new[] { "(set-option)", "(set-option :produce-models true)", "(set-option :a 1 2 3 (4))", "(echo)", "(echo \"a\" \"b\")", "(push)", "(push 1 2)" })
            SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.COMMAND_NOT_ALLOWED);
    }

    [Test]
    public void LongUnknownCommandNameIsCutInTheRefusalTest()
    {
        var name = "frob" + new string('z', 5_000);

        var refusal = SmtSanitizerChecks.Refuse($"({name} 1)", SmtRefusalCodes.COMMAND_NOT_ALLOWED);

        Assert.That(refusal.Command, Is.EqualTo("frob" + new string('z', 57) + "..."));
        Assert.That(refusal.Reason, Does.Not.Contain("zz"));
    }

    [Test]
    public void CommandNameWithLineBreaksIsShownOnOneLineTest()
    {
        var refusal = SmtSanitizerChecks.Refuse("(|two\nlines\tand a tab| 1)", SmtRefusalCodes.COMMAND_NOT_ALLOWED);

        Assert.That(refusal.Command, Is.EqualTo("two lines and a tab"));
    }

    #endregion

    #region Tools

    private static void AssertRefusedByName(string name, string reason)
    {
        foreach (var (text, line) in new[] { ($"({name})", 1), ($"(declare-const x Int)\n\n({name} x 1 \"text\" (y))\n(assert x)", 3) })
        {
            var refusal = SmtSanitizerChecks.Refuse(text, SmtRefusalCodes.COMMAND_NOT_ALLOWED);

            Assert.That(refusal.Command, Is.EqualTo(name));
            Assert.That(refusal.Line, Is.EqualTo(line));
            Assert.That(refusal.Reason, Is.EqualTo(reason));
        }
    }

    private static IEnumerable<string> KeptNames()
    {
        return KEPT.Keys;
    }

    private static IEnumerable<string> StrippedNames()
    {
        return STRIPPED;
    }

    private static IEnumerable<string> IncrementalNames()
    {
        return INCREMENTAL;
    }

    private static IEnumerable<string> OtherNames()
    {
        return OTHER;
    }

    private static IEnumerable<string> InventedNames()
    {
        return INVENTED;
    }

    #endregion
}
