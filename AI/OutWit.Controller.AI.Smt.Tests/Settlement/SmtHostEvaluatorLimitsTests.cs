using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Settlement;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Settlement;

/// <summary>
/// The host reads a node's model under limits of the reader's own: its tokens, its nesting, the
/// digits of a number, the bits of a literal and the characters of a string (these four at what the
/// evaluator could use at the most), and the length of a name (at what a problem holds). A model
/// past one is unreadable before it is held, and the reading of it costs no more than the part read
/// up to the limit. Every limit stands just where it says: a text at it is read, a text one past it
/// is not.
/// </summary>
[TestFixture]
public sealed class SmtHostEvaluatorLimitsTests
{
    #region Constants

    private const string NON_NEGATIVE = "(declare-const x Int)(assert (>= x 0))";
    private const string BITS = "(declare-const b (_ BitVec 8))(assert (= b b))";

    // Sixteen megabytes of one-letter atoms in one list, as a node may write a model.
    private const int ATOMS = 8_000_000;

    // What reading such a model costs: measured at about 1.6 s and 677 MiB of allocations on a
    // desktop machine. It is under the limit of tokens (a token every two characters a session may
    // print) and is read whole, then is no model; its package pays four steps a character for it,
    // 64,010,008 steps, so a package's budget pays for 46 of them.
    private const long ATOMS_MAX_ALLOCATED_BYTES = 900L * 1024 * 1024;
    private const int FLOOD_MAX_SECONDS = 10;

    // The densest table the pinned solver prints, of a function of four integers: an entry a line.
    private const string TABLE_HEAD = "(\n  (define-fun g4 ((x!0 Int) (x!1 Int) (x!2 Int) (x!3 Int)) Int\n";
    private const string TABLE_ENTRY = "    (ite (and (= x!0 2) (= x!1 0) (= x!2 0) (= x!3 0)) 2\n";
    private const string TABLE_DEFAULT = "    0";

    #endregion

    #region Flood Tests

    [Test]
    public void ModelOfTheDensestShapeThatFillsASessionsOutputIsReadTest()
    {
        // The table of a function of four integers, as the solver prints it, as long as a
        // session's output: 0.46 tokens a character, nested as deep as the table is long.
        var entries = (16 * 1024 * 1024 - TABLE_HEAD.Length - TABLE_DEFAULT.Length) / (TABLE_ENTRY.Length + 1) - 2;
        var text = new StringBuilder(16 * 1024 * 1024).Append(TABLE_HEAD);
        for (var entry = 0; entry < entries; entry++)
            text.Append(TABLE_ENTRY);

        var model = text.Append(TABLE_DEFAULT).Append(')', entries).Append(")\n)").ToString();

        var (read, seconds, allocated) = Measured(() => SmtModel.Read(model, null, SmtHostEvaluator.LIMITS).IsReadable);

        TestContext.Out.WriteLine($"{model.Length} characters, {entries} entries: read in {seconds:F2} s, {allocated / (1024 * 1024)} MiB allocated");
        Assert.That(model.Length, Is.LessThanOrEqualTo(16 * 1024 * 1024).And.GreaterThan(16 * 1024 * 1024 - 4 * (TABLE_ENTRY.Length + 1)));
        Assert.That(read, Is.True, "a model a session may print is read");
    }

    [Test]
    public void TextOfParenthesesAloneThatFillsASessionsOutputIsRefusedTest()
    {
        // One token a character: twice the tokens a model may have.
        var text = new StringBuilder(16 * 1024 * 1024).Append('(').Insert(1, "()", 16 * 1024 * 1024 / 2 - 1).Append(')').ToString();

        var (fault, seconds, allocated) = Measured(() => Fault(text));

        TestContext.Out.WriteLine($"refused in {seconds:F2} s, {allocated / (1024 * 1024)} MiB allocated");
        Assert.That(fault, Is.EqualTo(SmtSyntaxFault.TooManyTokens));
        Assert.That(seconds, Is.LessThan(FLOOD_MAX_SECONDS));
    }

    [Test]
    public void ModelOfSixteenMegabytesOfAtomsIsReadAndItsPackagePaysForItTest()
    {
        var text = new StringBuilder(2 * ATOMS + 2).Append('(');
        for (var atom = 0; atom < ATOMS; atom++)
            text.Append("a ");

        var model = text.Append(')').ToString();
        var problem = SmtHostChecks.Problem(Line(NON_NEGATIVE));

        var (check, seconds, allocated) = Measured(() => SmtHostChecks.Check(problem, SmtSettleRig.Sat(model)));
        var price = SmtPackageBudget.Price(new SmtValidation { Class = SmtValidationClass.Partial, Reason = SmtValidationReasons.UNREADABLE_MODEL }, model.Length);

        TestContext.Out.WriteLine($"read in {seconds:F2} s, {allocated / (1024 * 1024)} MiB allocated; its package pays {price} steps for it, {SmtPackageBudget.DEFAULT_STEPS / price} such models a package");
        Assert.Multiple(() =>
        {
            Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "")), "read, and no model");
            Assert.That(allocated, Is.LessThan(ATOMS_MAX_ALLOCATED_BYTES));
            Assert.That(seconds, Is.LessThan(FLOOD_MAX_SECONDS));
            Assert.That(price, Is.EqualTo(SmtPackageBudget.MODEL_STEPS + 4L * model.Length), "four steps a character");
        });
    }

    #endregion

    #region Limit Tests

    [Test]
    public void ReaderLimitsOfAModelAreTheStatedNumbersTest()
    {
        var limits = SmtHostEvaluator.LIMITS;

        Assert.Multiple(() =>
        {
            Assert.That(limits.MaxCharacters, Is.EqualTo(2 * 16 * 1024 * 1024));
            Assert.That(limits.MaxTokens, Is.EqualTo(16 * 1024 * 1024 / 2), "a token every two characters a session may print");
            Assert.That(limits.MaxDepth, Is.EqualTo(limits.MaxTokens / 2), "as deep as the tokens let a text nest");
            Assert.That(limits.MaxLiteralBits, Is.EqualTo(SmtEvaluationBudget.DEFAULT_BIT_VECTOR_BITS));
            Assert.That(limits.MaxStringCharacters, Is.EqualTo(SmtEvaluationBudget.DEFAULT_STRING_CHARACTERS));
            Assert.That(limits.MaxSymbolCharacters, Is.EqualTo(SmtReaderLimits.PROBLEM_CHARACTERS));

            // A numeral of the digits allowed may be wider than the integer ceiling; one of the
            // digits of the widest integer the ceiling holds is always allowed.
            var widest = (int)Math.Floor(SmtEvaluationBudget.DEFAULT_INTEGER_BITS * Math.Log10(2)) + 1;
            Assert.That(limits.MaxNumeralDigits, Is.GreaterThanOrEqualTo(widest));
            Assert.That((limits.MaxNumeralDigits - 1) * Math.Log2(10), Is.GreaterThan(SmtEvaluationBudget.DEFAULT_INTEGER_BITS));
        });
    }

    [Test]
    public void NestingPastTheEvaluatorsCeilingIsReadTest()
    {
        // A function's table nests as deep as it is long, and the evaluator walks it without
        // nesting: the reader does not refuse a model for its depth before its tokens do.
        var depth = 2 * SmtEvaluationBudget.DEPTH_CEILING;

        Assert.That(Fault(new string('(', depth) + new string(')', depth)), Is.Null);
    }

    [Test]
    public void NumeralOfTheDigitsAllowedIsReadAndLongerIsNotTest()
    {
        var digits = SmtHostEvaluator.MAX_MODEL_NUMERAL_DIGITS;

        Assert.Multiple(() =>
        {
            Assert.That(Fault("1" + new string('0', digits - 1)), Is.Null);
            Assert.That(Fault("1" + new string('0', digits)), Is.EqualTo(SmtSyntaxFault.NumeralTooLong));
            Assert.That(Fault("1." + new string('0', digits - 1)), Is.Null, "a decimal's digits, both sides together");
            Assert.That(Fault("1." + new string('0', digits)), Is.EqualTo(SmtSyntaxFault.NumeralTooLong));
        });
    }

    [Test]
    public void LiteralOfTheWidestBitVectorIsReadAndWiderIsNotTest()
    {
        var bits = SmtEvaluationBudget.DEFAULT_BIT_VECTOR_BITS;

        Assert.Multiple(() =>
        {
            Assert.That(Fault("#b" + new string('1', bits)), Is.Null);
            Assert.That(Fault("#b" + new string('1', bits + 1)), Is.EqualTo(SmtSyntaxFault.LiteralTooWide));
            Assert.That(Fault("#x" + new string('f', bits / 4)), Is.Null);
            Assert.That(Fault("#x" + new string('f', bits / 4 + 1)), Is.EqualTo(SmtSyntaxFault.LiteralTooWide));
        });
    }

    [Test]
    public void StringOfTheLongestStringIsReadAndLongerIsNotTest()
    {
        var characters = SmtEvaluationBudget.DEFAULT_STRING_CHARACTERS;

        Assert.Multiple(() =>
        {
            Assert.That(Fault("\"" + new string('s', characters) + "\""), Is.Null);
            Assert.That(Fault("\"" + new string('s', characters + 1) + "\""), Is.EqualTo(SmtSyntaxFault.StringTooLong));
        });
    }

    [Test]
    public void NameOfTheLongestAProblemHoldsIsReadAndLongerIsNotTest()
    {
        var characters = SmtReaderLimits.PROBLEM_CHARACTERS;

        Assert.Multiple(() =>
        {
            Assert.That(Fault(new string('n', characters)), Is.Null);
            Assert.That(Fault(new string('n', characters + 1)), Is.EqualTo(SmtSyntaxFault.SymbolTooLong));
            Assert.That(Fault("|" + new string('n', characters) + "|"), Is.Null, "between bars, the bars not counted");
            Assert.That(Fault("|" + new string('n', characters + 1) + "|"), Is.EqualTo(SmtSyntaxFault.SymbolTooLong));
            Assert.That(Fault(":" + new string('n', characters)), Is.Null, "a keyword, its colon not counted");
            Assert.That(Fault(":" + new string('n', characters + 1)), Is.EqualTo(SmtSyntaxFault.SymbolTooLong));
        });
    }

    [Test]
    public void ProblemIsReadWithoutALimitOfItsNamesTest()
    {
        // The limit of a name is the model's only: a problem's names are bounded by its characters.
        Assert.That(SmtReaderLimits.Problem.MaxSymbolCharacters, Is.EqualTo(int.MaxValue));
        Assert.That(SmtRefusalCodes.Of(SmtSyntaxFault.SymbolTooLong), Is.EqualTo(SmtRefusalCodes.SYNTAX_ERROR));
    }

    [Test]
    public void ModelPastALimitIsUnreadableToTheHostTest()
    {
        var problem = SmtHostChecks.Problem(Line(NON_NEGATIVE));
        var bits = SmtHostChecks.Problem(Line(BITS));
        var digits = SmtHostEvaluator.MAX_MODEL_NUMERAL_DIGITS;

        Assert.Multiple(() =>
        {
            Assert.That(SmtHostChecks.Check(problem, SmtSettleRig.Sat($"((define-fun x () Int 1{new string('0', digits)}))")),
                Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "")), "a numeral past the digits allowed");
            Assert.That(SmtHostChecks.Check(bits, SmtSettleRig.Sat($"((define-fun b () (_ BitVec 8) #b{new string('0', SmtEvaluationBudget.DEFAULT_BIT_VECTOR_BITS + 1)}))")),
                Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "")), "a literal past the widest bit-vector");
            Assert.That(SmtHostChecks.Check(problem, SmtSettleRig.Sat(Nested(SmtEvaluationBudget.DEPTH_CEILING))),
                Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.BUDGET_DEPTH, "")), "a definition nested past the evaluator's ceiling is read, and its check ends there");
            Assert.That(SmtHostChecks.Check(problem, SmtSettleRig.Sat("((define-fun x () Int 1))")), Is.EqualTo((SmtRunWords.VALIDATED, "", "")), "an honest model is read as before");
        });
    }

    #endregion

    #region Tools

    /// <summary>What a piece of work gave, how long it took and what it allocated on this thread.</summary>
    private static (T Result, double Seconds, long Allocated) Measured<T>(Func<T> work)
    {
        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var result = work();
        clock.Stop();
        return (result, clock.Elapsed.TotalSeconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    /// <summary>What the host's reader of models says of a text: null when it reads it, else the fault.</summary>
    private static SmtSyntaxFault? Fault(string text)
    {
        try
        {
            SmtReader.Read(text, SmtHostEvaluator.LIMITS);
            return null;
        }
        catch (SmtSyntaxException exception)
        {
            return exception.Fault;
        }
    }

    /// <summary>A model whose one definition is a sum nested to a depth, two lists deeper in the model.</summary>
    private static string Nested(int depth)
    {
        return "((define-fun x () Int " + string.Concat(Enumerable.Repeat("(+ 0 ", depth)) + "1" + new string(')', depth) + "))";
    }

    private static string Line(string smt2)
    {
        return JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = "p0", ["smt2"] = smt2 });
    }

    #endregion
}
