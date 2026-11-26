using System.Text;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Lang;

/// <summary>
/// Which constants of a problem have values that hold elements of an uninterpreted sort
/// (<see cref="SmtUninterpretedSorts"/>): the sort itself; a sort of the theories over it, by any
/// argument; an alias or a datatype, by what it stands for or what its fields are, and by a
/// parameter only where a value has it. The problem is read in the order of its commands, as the
/// solver reads it, and every sort once: a sort nested as deep as the reader takes one, and
/// thousands of datatypes that name each other, are read without a deep call stack and without
/// being read again.
/// </summary>
[TestFixture]
public sealed class SmtUninterpretedSortsTests
{
    #region Constants

    private const string U = "(declare-sort U 0)";

    #endregion

    #region Sort Tests

    [Test]
    public void AProblemThatDeclaresNoSortHoldsNoneTest()
    {
        Assert.That(Held("(declare-const x Int)(declare-const a (Array Int Bool))", "x", "a"), Is.Empty);
    }

    [Test]
    public void AConstantOfAnUninterpretedSortHoldsItTest()
    {
        var held = Held(U + "(declare-sort P 2)(declare-const x U)(declare-fun y () (P Int U))(declare-const n Int)(declare-fun f (U) U)", "x", "y", "n", "f");

        // A sort with arguments is uninterpreted whatever its arguments are: they are not looked into.
        Assert.That(held, Is.EqualTo(new Dictionary<string, string> { ["x"] = "U", ["y"] = "P" }));
    }

    [Test]
    public void OnlyTheConstantsAskedAboutAreAnsweredTest()
    {
        Assert.That(Held(U + "(declare-const x U)(declare-const y U)", "y"), Is.EqualTo(new Dictionary<string, string> { ["y"] = "U" }));
    }

    [TestCase("(Array U Int)")]
    [TestCase("(Array Int U)")]
    [TestCase("(Array Int Int U)")]
    [TestCase("(Seq U)")]
    [TestCase("(-> Int U)")]
    [TestCase("(Set U)")]
    [TestCase("(Array Int (Seq (Array U Bool)))")]
    [TestCase("(SomeSortNobodyDeclared Int U)")]
    public void ASortOfTheTheoriesHoldsWhatAnyOfItsArgumentsHoldsTest(string sort)
    {
        Assert.That(Held(U + $"(declare-const x {sort})", "x"), Is.EqualTo(new Dictionary<string, string> { ["x"] = "U" }));
    }

    [TestCase("(_ BitVec 8)")]
    [TestCase("(Array Int (_ FloatingPoint 8 24))")]
    [TestCase("(Seq String)")]
    [TestCase("Bool")]
    public void ASortOfTheTheoriesOverNoUninterpretedSortHoldsNoneTest(string sort)
    {
        Assert.That(Held(U + $"(declare-const u U)(declare-const x {sort})", "x"), Is.Empty);
    }

    #endregion

    #region Alias Tests

    [TestCase("(define-sort V () U)", "V", true)]
    [TestCase("(define-sort Id (T) T)", "(Id U)", true)]
    [TestCase("(define-sort Id (T) T)", "(Id Int)", false)]
    [TestCase("(define-sort Map (K V) (Array K V))", "(Map Int U)", true)]
    [TestCase("(define-sort First (A B) A)", "(First U Int)", true)]
    [TestCase("(define-sort First (A B) A)", "(First Int U)", false)]
    [TestCase("(define-sort K (T) Int)", "(K U)", false)]
    [TestCase("(define-sort K (T) (Array Int U))", "(K Int)", true)]
    [TestCase("(define-sort One (T) (Seq T))(define-sort Two (T) (One (One T)))", "(Two U)", true)]
    [TestCase("(define-sort Drop (T) Bool)(define-sort Two (T) (Array (Drop T) Int))", "(Two U)", false)]
    public void AnAliasHoldsWhatTheSortItStandsForHoldsTest(string aliases, string sort, bool holds)
    {
        Assert.That(Held(U + aliases + $"(declare-const x {sort})", "x").ContainsKey("x"), Is.EqualTo(holds));
    }

    #endregion

    #region Datatype Tests

    [TestCase("(declare-datatype D ((mk (u U) (n Int))))", "D", true)]
    [TestCase("(declare-datatype D ((a) (b (n Int))))", "D", false)]
    [TestCase("(declare-datatype Opt (par (T) ((none) (some (get T)))))", "(Opt U)", true)]
    [TestCase("(declare-datatype Opt (par (T) ((none) (some (get T)))))", "(Opt Int)", false)]
    [TestCase("(declare-datatype Ph (par (T) ((ph (val Bool)))))", "(Ph U)", false)]
    [TestCase("(declare-datatype Pr (par (A B) ((pr (fst A) (snd B)))))", "(Pr Int U)", true)]
    [TestCase("(declare-datatype Half (par (A B) ((half (fst A)))))", "(Half Int U)", false)]
    [TestCase("(declare-datatype Lst (par (T) ((nl) (cns (hd T) (tl (Lst T))))))", "(Lst (Lst U))", true)]
    [TestCase("(declare-datatype Tails (par (T) ((nl) (cns (tl (Tails T))))))", "(Tails U)", false)]
    [TestCase("(declare-datatype D ((mk (u U))))(declare-datatype Lst (par (T) ((nl) (cns (hd T) (tl (Lst T))))))", "(Lst D)", true)]
    [TestCase("(declare-datatype Ph (par (T) ((ph (val Bool)))))(declare-datatype W (par (T) ((w (in (Ph T))))))", "(W U)", false)]
    [TestCase("(declare-datatype Opt (par (T) ((none) (some (get T)))))(declare-datatype W (par (T) ((w (in (Array Int (Opt T)))))))", "(W U)", true)]
    public void ADatatypeHoldsWhatTheFieldsOfItsConstructorsHoldTest(string datatypes, string sort, bool holds)
    {
        Assert.That(Held(U + datatypes + $"(declare-const x {sort})", "x").ContainsKey("x"), Is.EqualTo(holds));
    }

    [Test]
    public void TheDatatypesOfOneDeclarationAreSettledWhicheverComesFirstTest()
    {
        // A names B before anything of B is known, and B names A back: what each holds is told
        // to the other when it turns out, in whichever order the two are declared.
        const string HOLDER_LAST = "(declare-datatypes ((A 0) (B 0)) (((a) (to-b (b B))) ((stop (u U)) (to-a (a2 A)))))";
        const string HOLDER_FIRST = "(declare-datatypes ((B 0) (A 0)) (((stop (u U)) (to-a (a2 A))) ((a) (to-b (b B)))))";
        const string PASSER_LAST = "(declare-datatypes ((A 1) (B 1)) ((par (T) ((a (x (B T))))) (par (S) ((b (y S))))))";
        const string PASSER_FIRST = "(declare-datatypes ((B 1) (A 1)) ((par (S) ((b (y S)))) (par (T) ((a (x (B T)))))))";
        const string NEVER = "(declare-datatypes ((A 1) (B 1)) ((par (T) ((a (x (B T))) (end))) (par (S) ((b (y (A S))) (end2)))))";

        Assert.Multiple(() =>
        {
            Assert.That(Held(U + HOLDER_LAST + "(declare-const x A)", "x"), Does.ContainKey("x"));
            Assert.That(Held(U + HOLDER_FIRST + "(declare-const x A)", "x"), Does.ContainKey("x"));
            Assert.That(Held(U + PASSER_LAST + "(declare-const x (A U))(declare-const y (A Int))", "x", "y").Keys, Is.EqualTo(new[] { "x" }));
            Assert.That(Held(U + PASSER_FIRST + "(declare-const x (A U))(declare-const y (A Int))", "x", "y").Keys, Is.EqualTo(new[] { "x" }));
            Assert.That(Held(U + NEVER + "(declare-const x (A U))", "x"), Is.Empty, "two datatypes that only hand a parameter to each other never put it in a value");
        });
    }

    #endregion

    #region Reading Tests

    [Test]
    public void ANameTheProblemGaveASortIsThatSortEvenUnderAParameterOfItsNameTest()
    {
        // The solver reads the declared sort where a parameter of an alias or of a datatype has
        // its name: the parameter is never used, and the values hold the declared sort.
        Assert.Multiple(() =>
        {
            Assert.That(Held(U + "(define-sort A (U) (Array Int U))(declare-const x (A Int))", "x"), Does.ContainKey("x"));
            Assert.That(Held(U + "(declare-datatype Box (par (U) ((box (get U)))))(declare-const x (Box Bool))", "x"), Does.ContainKey("x"));
        });
    }

    [Test]
    public void ANameIsWhatWasDeclaredBeforeItsUseTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Held("(declare-const x U)" + U, "x"), Is.Empty, "a sort used before its declaration is the solver's error to report");
            Assert.That(Held("(define-sort V () U)" + U + "(declare-const x V)", "x"), Is.Empty);
            Assert.That(Held(U + "(declare-const x Int)(declare-const x U)", "x"), Is.Empty, "a constant declared twice is its first declaration");
            Assert.That(Held(U + "(declare-const x U)(declare-const x Int)", "x"), Does.ContainKey("x"));
            Assert.That(Held("(define-sort V () Int)(declare-sort V 0)(declare-const x V)", "x"), Is.Empty, "a name that is taken stays what it was");
            Assert.That(Held(U + "(define-sort U () Int)(declare-const x U)", "x"), Does.ContainKey("x"));
        });
    }

    #endregion

    #region Scale Tests

    [Test]
    [CancelAfter(120_000)]
    public void ASortNestedAsDeepAsTheReaderTakesOneIsReadWithoutADeepCallStackTest()
    {
        // (declare-const x (Array Int (Array Int ... U))): the innermost array is at the reader's depth.
        var arrays = SmtReaderLimits.PROBLEM_DEPTH - 1;
        var text = new StringBuilder(U).Append("(declare-const x ");
        for (var level = 0; level < arrays; level++)
            text.Append("(Array Int ");

        text.Append('U').Append(')', arrays).Append(")(declare-const y ");
        for (var level = 0; level < arrays; level++)
            text.Append("(Array Int ");

        text.Append("Bool").Append(')', arrays).Append(')');

        Assert.That(Held(text.ToString(), "x", "y").Keys, Is.EqualTo(new[] { "x" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    [CancelAfter(120_000)]
    public void ThousandsOfDatatypesThatNameEachOtherAreSettledTest(bool holderFirst)
    {
        // One declaration of twenty thousand datatypes in a chain, each with a field of the next,
        // and one end of the chain with a field of the uninterpreted sort: whichever end is
        // declared first, the other end holds it.
        const int COUNT = 20_000;
        var order = holderFirst ? Enumerable.Range(0, COUNT).Reverse().ToList() : Enumerable.Range(0, COUNT).ToList();

        var text = new StringBuilder(U).Append("(declare-datatypes (");
        foreach (var index in order)
            text.Append($"(D{index} 0)");

        text.Append(") (");
        foreach (var index in order)
            text.Append(index == COUNT - 1 ? $"((c{index} (f{index} U)))" : $"((c{index} (f{index} D{index + 1})))");

        text.Append("))(declare-const x D0)");

        Assert.That(Held(text.ToString(), "x"), Is.EqualTo(new Dictionary<string, string> { ["x"] = "U" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    [CancelAfter(120_000)]
    public void ThousandsOfDatatypesThatHandAParameterOnAreSettledTest(bool userFirst)
    {
        // The same chain with a parameter: each hands its parameter to the next, and one end puts
        // it in a field. The argument of the other end is in its values.
        const int COUNT = 20_000;
        var order = userFirst ? Enumerable.Range(0, COUNT).Reverse().ToList() : Enumerable.Range(0, COUNT).ToList();

        var text = new StringBuilder(U).Append("(declare-datatypes (");
        foreach (var index in order)
            text.Append($"(D{index} 1)");

        text.Append(") (");
        foreach (var index in order)
            text.Append(index == COUNT - 1 ? $"(par (T) ((c{index} (f{index} T))))" : $"(par (T) ((c{index} (f{index} (D{index + 1} T)))))");

        text.Append("))(declare-const x (D0 U))(declare-const y (D0 Int))");

        Assert.That(Held(text.ToString(), "x", "y"), Is.EqualTo(new Dictionary<string, string> { ["x"] = "U" }));
    }

    #endregion

    #region Tools

    private static IReadOnlyDictionary<string, string> Held(string text, params string[] constants)
    {
        var sanitized = SmtSanitizer.Sanitize(text);
        Assert.That(sanitized.Refusal, Is.Null, () => $"the problem of the test is refused: {sanitized.Refusal?.Code}: {sanitized.Refusal?.Reason}");

        return SmtUninterpretedSorts.Held(sanitized.Problem!, constants.ToHashSet(StringComparer.Ordinal));
    }

    #endregion
}
