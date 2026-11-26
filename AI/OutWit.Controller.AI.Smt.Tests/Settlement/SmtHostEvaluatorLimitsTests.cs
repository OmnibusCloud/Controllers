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
/// The host reads a node's model under limits of the reader's own, each at what the evaluator
/// could use at the most: tokens, nesting, the length of a name, the digits of a number, the bits
/// of a literal, the characters of a string. A model past one is unreadable before it is held, and
/// the reading of it costs no more than the part read up to the limit. Every limit stands just
/// where it says: a text at it is read, a text one past it is not.
/// </summary>
[TestFixture]
public sealed class SmtHostEvaluatorLimitsTests
{
    #region Constants

    private const string NON_NEGATIVE = "(declare-const x Int)(assert (>= x 0))";
    private const string BITS = "(declare-const b (_ BitVec 8))(assert (= b b))";

    // Sixteen megabytes of one-letter atoms in one list, as a node may write a model.
    private const int ATOMS = 8_000_000;

    // What reading such a model may cost before it is refused: measured at about 0.3 s and 300 MB
    // of allocations on a desktop machine (the reader stops at its limit of tokens); without the
    // limit it read all eight million atoms.
    private const long ATOMS_MAX_ALLOCATED_BYTES = 450L * 1024 * 1024;
    private const int ATOMS_MAX_SECONDS = 5;

    #endregion

    #region Atoms Tests

    [Test]
    public void ModelOfSixteenMegabytesOfAtomsIsRefusedByTheReaderTest()
    {
        var text = new StringBuilder(2 * ATOMS + 2).Append('(');
        for (var atom = 0; atom < ATOMS; atom++)
            text.Append("a ");

        var model = text.Append(')').ToString();
        var problem = SmtHostChecks.Problem(Line(NON_NEGATIVE));

        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        var check = SmtHostChecks.Check(problem, SmtSettleRig.Sat(model));
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        TestContext.Out.WriteLine($"refused in {clock.Elapsed.TotalSeconds:F2} s, {allocated / (1024 * 1024)} MiB allocated");
        Assert.Multiple(() =>
        {
            Assert.That(check, Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "")));
            Assert.That(allocated, Is.LessThan(ATOMS_MAX_ALLOCATED_BYTES), "the reader stopped at its limit of tokens");
            Assert.That(clock.Elapsed.TotalSeconds, Is.LessThan(ATOMS_MAX_SECONDS));
        });
    }

    #endregion

    #region Limit Tests

    [Test]
    public void LimitsAreWhatTheEvaluatorCouldUseTest()
    {
        var limits = SmtHostEvaluator.LIMITS;

        Assert.Multiple(() =>
        {
            Assert.That(limits.MaxCharacters, Is.EqualTo(2 * 16 * 1024 * 1024));
            Assert.That(limits.MaxTokens, Is.EqualTo(SmtHostEvaluator.MAX_MODEL_TOKENS));
            Assert.That(limits.MaxDepth, Is.EqualTo(SmtEvaluationBudget.DEPTH_CEILING));
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
    public void NestingAtTheEvaluatorsCeilingIsReadAndDeeperIsNotTest()
    {
        var ceiling = SmtEvaluationBudget.DEPTH_CEILING;

        Assert.Multiple(() =>
        {
            Assert.That(Fault(new string('(', ceiling) + new string(')', ceiling)), Is.Null);
            Assert.That(Fault(new string('(', ceiling + 1) + new string(')', ceiling + 1)), Is.EqualTo(SmtSyntaxFault.NestingTooDeep));
        });
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
                Is.EqualTo((SmtRunWords.PARTIAL, SmtValidationReasons.UNREADABLE_MODEL, "")), "a definition nested past the evaluator's highest ceiling");
            Assert.That(SmtHostChecks.Check(problem, SmtSettleRig.Sat("((define-fun x () Int 1))")), Is.EqualTo((SmtRunWords.VALIDATED, "", "")), "an honest model is read as before");
        });
    }

    #endregion

    #region Tools

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
