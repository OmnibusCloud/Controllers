using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

/// <summary>
/// The price of a name. A name is found by its text: a table hashes the whole name on every
/// lookup and compares it on a hit, so reading a name costs its length, each time. The step
/// budget counts that: a step for every 64 characters, every time the name is read from end to
/// end. A name shorter than 64 characters pays nothing of its own, so a model of ordinary names
/// costs what it always did; a long name is still taken, and pays.
/// <para>
/// Each role a name can stand in is here once, in a small recursion: the model with a long name
/// costs more than the same model with a short one by exactly the times the name is read, a
/// step for every 64 characters each time.
/// </para>
/// </summary>
[TestFixture]
public class SmtModelValidatorNamePriceTests
{
    #region Constants

    private const string PROBLEM = "(declare-const x Int)(assert (= x 0))";
    private const string PLACE = "NAME";

    /// <summary>A long name of the roles' table: a hundred times the characters one step pays for.</summary>
    private const int WORDS = 100;

    /// <summary>The name of the model that once held a validation for minutes: a megabyte of model, the name written four times.</summary>
    private const int RUNAWAY_CHARACTERS = 250_000;

    private const int RUNAWAY_DEPTH = 19;

    /// <summary>Twenty times the 0.25 s the runaway model takes to use the budget up.</summary>
    private const double RUNAWAY_BOUND_SECONDS = 5;

    #endregion

    #region Line Tests

    [TestCaseSource(nameof(Roles))]
    public void NameShorterThanTheLineCostsWhatAShortOneCostsTest(string role, string problem, string model, int reads)
    {
        var shortest = Validate(problem, model, 1);
        var below = Validate(problem, model, SmtMeter.NAME_CHARACTERS - 1);

        Assert.That(shortest.Class, Is.EqualTo(SmtValidationClass.Validated), shortest.ToString());
        Assert.That(below.Is(shortest), Is.True, $"{role}: {below} against {shortest}");
    }

    [TestCaseSource(nameof(Roles))]
    public void LongNamePaysForEveryTimeItIsReadTest(string role, string problem, string model, int reads)
    {
        var shortest = Validate(problem, model, 1);
        var atTheLine = Validate(problem, model, SmtMeter.NAME_CHARACTERS);
        var longest = Validate(problem, model, WORDS * SmtMeter.NAME_CHARACTERS);

        // The class and every count but the steps are the short name's: the price changes no answer.
        Assert.That(longest.Class, Is.EqualTo(SmtValidationClass.Validated), longest.ToString());
        Assert.That(longest.True, Is.EqualTo(shortest.True));
        Assert.That(longest.Defined, Is.EqualTo(shortest.Defined));
        Assert.That(atTheLine.Steps - shortest.Steps, Is.EqualTo(reads), role);
        Assert.That(longest.Steps - shortest.Steps, Is.EqualTo((long)reads * WORDS), role);
    }

    #endregion

    #region Budget Tests

    [Test]
    public void RecursionOverAParameterWithALongNameEndsAtTheStepBudgetTest()
    {
        // A model's own function whose parameter has a name of 250,000 characters, called a
        // million times: the name is bound once a call and read three times. With a short name
        // the model is validated in 0.3 s. Each read of the long one hashes half a megabyte:
        // priced at a step whatever the length, the validation ran its million calls inside the
        // budget and took 150 s. Priced by the length it ends at the budget after 1,280 calls,
        // in 0.25 s.
        var model = Parameter(RUNAWAY_DEPTH);
        var ordinary = SmtCheck.Validate(PROBLEM, model.Replace(PLACE, "n"));

        var (validation, elapsed) = SmtTimed.Validate(PROBLEM, model.Replace(PLACE, new string('n', RUNAWAY_CHARACTERS)));

        Assert.That(ordinary.Class, Is.EqualTo(SmtValidationClass.Validated), ordinary.ToString());
        Assert.That(validation.Class, Is.EqualTo(SmtValidationClass.Partial), validation.ToString());
        Assert.That(validation.Reason, Is.EqualTo(SmtValidationReasons.BUDGET_STEPS));
        Assert.That(validation.Steps, Is.EqualTo(SmtEvaluationBudget.DEFAULT_STEPS));
        Assert.That(elapsed.TotalSeconds, Is.LessThan(RUNAWAY_BOUND_SECONDS), $"the budget was used up in {elapsed.TotalSeconds:F2} s");
    }

    [Test]
    public void LongNameOfAnHonestModelIsTakenAndPaidForTest()
    {
        // A lab may name a constant with ten thousand characters: its model is checked as any
        // other, a true answer validated and a wrong one failed, for a few hundred steps more.
        var name = new string('c', 10_000);
        var problem = $"(declare-const {name} Int)(declare-const y Int)(assert (> {name} y))(assert (= (+ {name} y) 12))";

        var right = SmtCheck.Validate(problem, $"((define-fun {name} () Int 7)(define-fun y () Int 5))");
        var wrong = SmtCheck.Validate(problem, $"((define-fun {name} () Int 5)(define-fun y () Int 7))");
        var ordinary = SmtCheck.Validate(problem.Replace(name, "c"), "((define-fun c () Int 7)(define-fun y () Int 5))");

        Assert.That(right.Class, Is.EqualTo(SmtValidationClass.Validated), right.ToString());
        Assert.That(wrong.Class, Is.EqualTo(SmtValidationClass.Failed), wrong.ToString());
        Assert.That(wrong.Detail, Is.EqualTo("1"));

        // The first read finds the name in the problem's table and among the remembered values,
        // then finds its definition and remembers the value (five reads more); the second read
        // finds it in the two tables.
        var words = name.Length / SmtMeter.NAME_CHARACTERS;
        Assert.That(right.Steps - ordinary.Steps, Is.EqualTo((2 + 5 + 2) * words));
    }

    [Test]
    public void DigitsOfABitVectorValueArePaidForBeforeTheyAreReadTest()
    {
        // (_ bv0...01 8): the digits are part of a name, and are looked at one by one before
        // the value is read. Zeros in front make it a value no solver writes.
        var plain = SmtCheck.Holds("(= (_ bv01 8) #x01)");
        var padded = SmtCheck.Holds($"(= (_ bv{new string('0', WORDS * SmtMeter.NAME_CHARACTERS)}1 8) #x01)");

        Assert.That(plain.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
        Assert.That(padded.Reason, Is.EqualTo(SmtValidationReasons.ILL_FORMED_TERM));
        Assert.That(padded.Steps - plain.Steps, Is.GreaterThanOrEqualTo(WORDS));
    }

    #endregion

    #region Tools

    /// <summary>
    /// Every role a name stands in, each in a recursion of fifteen calls: the role, the problem,
    /// the model (the name is written as <c>NAME</c> in both), and how many times the model's
    /// evaluation reads the name from end to end.
    /// </summary>
    private static IEnumerable<TestCaseData> Roles()
    {
        const string datatype = "(declare-datatypes ((P 0)) (((mk (fst Int)))))";
        const string recursion = "(h!0 (- n 1)) (h!0 (- n 1))";

        // A parameter: bound once a call; read three times in a call that calls on, once in a last one.
        yield return Role("parameter", PROBLEM, Parameter(3), 7 * (1 + 3) + 8 * (1 + 1));

        // A variable of a let: checked and told apart (two), bound and taken back (three), then read twice.
        yield return Role("let", PROBLEM,
            $"((define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (let ((NAME (- n 1))) (+ (h!0 NAME) (h!0 NAME))))) {X(3)})", 7 * (2 + 3 + 2));

        // A variable of a pattern: checked and told apart (two), bound and taken back (four), then read as a parameter is.
        yield return Role("pattern", datatype + PROBLEM,
            "((define-fun h!0 ((p P)) Int (match p (((mk NAME) (ite (<= NAME 0) 0 (+ (h!0 (mk (- NAME 1))) (h!0 (mk (- NAME 1))))))))) (define-fun x () Int (h!0 (mk 3))))",
            7 * (2 + 4 + 3) + 8 * (2 + 4 + 1));

        // The variable of a lambda: checked (two), found in the body (one), bound and taken back when the array is read (three), read twice.
        yield return Role("lambda", PROBLEM,
            $"((define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (select (lambda ((NAME Int)) (+ NAME NAME)) 0) {recursion}))) {X(3)})", 7 * (2 + 1 + 3 + 2));

        // A function the model adds: the problem's table and the model's functions at every call; found and remembered once (two).
        yield return Role("function", PROBLEM,
            "((define-fun NAME ((n Int)) Int (ite (<= n 0) 0 (+ (NAME (- n 1)) (NAME (- n 1))))) (define-fun x () Int (NAME 3)))", 15 * 2 + 2);

        // A constant the model adds: the local names, the problem's table and the remembered values at every read; found and remembered once (five).
        yield return Role("constant of the model", PROBLEM,
            $"((define-fun NAME () Int 1) (define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (h!0 (- n NAME)) (h!0 (- n NAME))))) {X(3)})", 14 * 3 + 5);

        // A constant the problem declares, read in the model's text: the same three tables.
        yield return Role("constant of the problem", "(declare-const NAME Int)(assert (= NAME 1))" + PROBLEM,
            $"((define-fun NAME () Int 1) (define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (h!0 (- n NAME)) (h!0 (- n NAME))))) {X(3)})", 14 * 3 + 5 + 2);

        // A constructor: the problem's table at every application.
        yield return Role("constructor", "(declare-datatypes ((P 0)) (((NAME (fst Int)))))" + PROBLEM,
            $"((define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (fst (NAME 0)) {recursion}))) {X(3)})", 7);

        // A sort: the five tables of sorts, each time the sort is read; it is written twice.
        yield return Role("sort", "(define-sort NAME () Int)" + PROBLEM,
            $"((define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (select ((as const (Array NAME NAME)) 0) n) {recursion}))) {X(3)})", 7 * 2 * 5);

        // A function taken as an array: the problem's table, the model's functions and the arrays made so far; found and remembered once (three).
        yield return Role("function as an array", PROBLEM,
            $"((define-fun NAME ((i Int)) Int 0) (define-fun h!0 ((n Int)) Int (ite (<= n 0) 0 (+ (select (_ as-array NAME) n) {recursion}))) {X(3)})", 7 * 3 + 3);

        // An element of a sort, compared with the same element made elsewhere: the two names are
        // compared character by character at every comparison; the element is found once (three and five).
        yield return Role("element", "(declare-sort A 0)(declare-const d A)(declare-const x Bool)(assert x)",
            "((declare-fun NAME () A) (define-fun g!1 ((a A) (b A) (n Int)) Bool (ite (<= n 0) true (and (= a b) (g!1 a b (- n 1)) (g!1 a b (- n 1))))) (define-fun x () Bool (g!1 NAME d 3)))",
            7 + 3 + 5);
    }

    private static TestCaseData Role(string role, string problem, string model, int reads)
    {
        return new TestCaseData(role, problem, model, reads).SetArgDisplayNames(role);
    }

    /// <summary>
    /// The model whose own function calls itself twice a level, down from a depth, over a
    /// parameter named <c>NAME</c>.
    /// </summary>
    private static string Parameter(int depth)
    {
        return $"((define-fun h!0 ((NAME Int)) Int (ite (<= NAME 0) 0 (+ (h!0 (- NAME 1)) (h!0 (- NAME 1))))) {X(depth)})";
    }

    private static string X(int depth)
    {
        return $"(define-fun x () Int (h!0 {depth}))";
    }

    private static SmtValidation Validate(string problem, string model, int characters)
    {
        var name = new string('N', characters);
        return SmtCheck.Validate(problem.Replace(PLACE, name), model.Replace(PLACE, name));
    }

    #endregion
}
