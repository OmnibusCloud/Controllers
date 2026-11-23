using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Tests.Eval;

[TestFixture]
public class SmtClosedScriptTests
{
    #region Layout Tests

    [Test]
    public void BuildTurnsEveryDeclarationTheModelDefinesIntoADefinitionTest()
    {
        const string problem = "(set-logic QF_LIA)(declare-const x Int)(declare-fun y () Int)(declare-fun f (Int Int) Int)(assert (= (f x y) 3))";
        const string model = "((define-fun x () Int 1) (define-fun y () Int (- 2)) (define-fun f ((x!0 Int) (x!1 Int)) Int (ite (and (= x!0 1) (= x!1 (- 2))) 3 0)))";

        var script = Build(problem, model);

        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(set-logic QF_LIA)",
            "(define-fun x () Int 1)",
            "(define-fun y () Int (- 2))",
            "(define-fun f ((x!0 Int) (x!1 Int)) Int (ite (and (= x!0 1) (= x!1 (- 2))) 3 0))",
            "(assert (= (f x y) 3))",
            "(check-sat)"
        }));
        Assert.That(script.Defined, Is.EqualTo(3));
        Assert.That(script.Free, Is.EqualTo(0));
    }

    [Test]
    public void BuildLeavesASymbolTheModelLeavesOutDeclaredTest()
    {
        const string problem = "(declare-const x Int)(declare-const y Int)(declare-fun f (Int) Int)(declare-fun g (Int) Int)(assert (= (f x) (g y)))";
        const string model = "((define-fun x () Int 1) (define-fun g ((a Int) (b Int)) Int 0))";

        var script = Build(problem, model);

        // y and f are not in the model; g is there with another number of arguments.
        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(define-fun x () Int 1)",
            "(declare-const y Int)",
            "(declare-fun f (Int) Int)",
            "(declare-fun g (Int) Int)",
            "(assert (= (f x) (g y)))",
            "(check-sat)"
        }));
        Assert.That(script.Defined, Is.EqualTo(1));
        Assert.That(script.Free, Is.EqualTo(3));
    }

    [Test]
    public void BuildPutsSortsThenElementsThenDefinitionsThenTheRestTest()
    {
        // The sort of the elements is declared after a constant in the problem: in the script
        // every sort stands before anything that uses one.
        const string problem = "(declare-const n Int)(declare-sort U 0)(declare-const u U)(define-fun twice ((k Int)) Int (* 2 k))(assert (= (twice n) 4))(declare-datatype Color ((red) (green)))(declare-const c Color)(assert (= c green))";
        const string model = "((declare-fun U!val!0 () U) (forall ((x U)) (= x U!val!0)) (define-fun u () U U!val!0) (define-fun n () Int 2) (define-fun c () Color green))";

        var script = Build(problem, model);

        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(declare-sort U 0)",
            "(declare-datatype Color ((red) (green)))",
            "(declare-fun U!val!0 () U)",
            "(define-fun n () Int 2)",
            "(define-fun u () U U!val!0)",
            "(define-fun c () Color green)",
            "(define-fun twice ((k Int)) Int (* 2 k))",
            "(assert (= (twice n) 4))",
            "(assert (= c green))",
            "(check-sat)"
        }));
    }

    [Test]
    public void BuildDeclaresAnElementTheModelNamesWithoutListingItTest()
    {
        // The solver lists the universe of S, and gives head a value of A, whose universe it
        // does not list: the element is declared with the listed ones, once.
        const string problem = "(declare-sort A 0)(declare-sort S 0)(declare-fun head (S) A)(declare-fun first (S) A)(declare-const s S)(assert (= s s))";
        const string model = "((declare-fun S!val!0 () S) (forall ((x S)) (= x S!val!0)) (define-fun s () S S!val!0) (define-fun head ((x!0 S)) A A!val!0) (define-fun first ((x!0 S)) A (ite (= x!0 S!val!0) A!val!0 A!val!1)))";

        var script = Build(problem, model);

        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(declare-sort A 0)",
            "(declare-sort S 0)",
            "(declare-fun S!val!0 () S)",
            "(declare-fun A!val!0 () A)",
            "(declare-fun A!val!1 () A)",
            "(define-fun head ((x!0 S)) A A!val!0)",
            "(define-fun first ((x!0 S)) A (ite (= x!0 S!val!0) A!val!0 A!val!1))",
            "(define-fun s () S S!val!0)",
            "(assert (= s s))",
            "(check-sat)"
        }));
        Assert.That(script.Unlisted, Is.EqualTo(2));
        Assert.That(script.Defined, Is.EqualTo(3));
    }

    [Test]
    public void BuildDeclaresOnlyElementsOfTheProblemsSortsTest()
    {
        // B is no sort of the problem, P has a parameter, A!val!x ends in no number, A!val!7 is
        // the problem's own constant, and A!val! has no number at all: only A!val!2 is an element.
        const string problem = "(declare-sort A)(declare-sort P 1)(declare-const A!val!7 A)(declare-fun f (Int) A)(assert (= (f 0) A!val!7))";
        const string model = "((define-fun A!val!7 () A A!val!2) (define-fun f ((x!0 Int)) A (ite (= x!0 0) A!val!2 (ite (= x!0 1) B!val!0 (ite (= x!0 2) P!val!0 (ite (= x!0 3) A!val!x (ite (= x!0 4) A!val! A!val!7)))))))";

        var script = Build(problem, model);

        Assert.That(script.Commands.Where(command => command.Head == "declare-fun").Select(command => command.ToString()), Is.EqualTo(new[] { "(declare-fun A!val!2 () A)" }));
        Assert.That(script.Unlisted, Is.EqualTo(1));
    }

    [Test]
    public void BuildDeclaresNoElementTheModelListsTest()
    {
        const string problem = "(declare-sort U 0)(declare-fun f (U) U)(declare-const u U)(assert (= (f u) u))";
        const string model = "((declare-fun U!val!0 () U) (declare-fun U!val!1 () U) (define-fun u () U U!val!1) (define-fun f ((x!0 U)) U (ite (= x!0 U!val!0) U!val!1 U!val!1)))";

        var script = Build(problem, model);

        Assert.That(script.Commands.Count(command => command.Head == "declare-fun"), Is.EqualTo(2));
        Assert.That(script.Unlisted, Is.EqualTo(0));
    }

    [Test]
    public void BuildPutsADefinitionAfterTheOnesItMentionsTest()
    {
        // The solver prints the function behind an array after the array, and a function
        // behind that one after it.
        const string problem = "(declare-const a (Array Int (Array Int Int)))(assert (= (select (select a 1) 2) 3))";
        const string model = "((define-fun a () (Array Int (Array Int Int)) (_ as-array k!2)) (define-fun k!2 ((x!0 Int)) (Array Int Int) (ite (= x!0 1) (_ as-array k!1) (_ as-array k!0))) (define-fun k!1 ((x!0 Int)) Int 3) (define-fun k!0 ((x!0 Int)) Int 0))";

        var names = Build(problem, model).Commands.Where(command => command.Head == "define-fun").Select(command => command.Items[1].Text).ToList();

        Assert.That(names, Is.EqualTo(new[] { "k!1", "k!0", "k!2", "a" }));
        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(OutWit.Controller.AI.Smt.Model.SmtValidationClass.Validated));
    }

    [Test]
    public void BuildWritesTheSortsOfTheProblemNotOfTheModelTest()
    {
        const string problem = "(define-sort Word () (_ BitVec 8))(declare-const w Word)(declare-fun next (Word) Word)(assert (= (next w) #x01))";
        const string model = "((define-fun w () (_ BitVec 8) #x00) (define-fun next ((x!0 (_ BitVec 8))) (_ BitVec 8) #x01))";

        var script = Build(problem, model);

        Assert.That(Lines(script), Does.Contain("(define-fun w () Word #x00)"));
        Assert.That(Lines(script), Does.Contain("(define-fun next ((x!0 Word)) Word #x01)"));
        Assert.That(Lines(script)[0], Is.EqualTo("(define-sort Word () (_ BitVec 8))"));
    }

    [Test]
    public void BuildTakesTheValueOfGetValueOverTheDefinitionOfGetModelTest()
    {
        var script = SmtClosedScript.Build(SmtCheck.Commands("(declare-const x Int)(assert (> x 0))"), SmtCheck.Model("((define-fun x () Int 9))", "((x 5))"));

        Assert.That(Lines(script)[0], Is.EqualTo("(define-fun x () Int 5)"));
    }

    [Test]
    public void BuildLeavesOutWhatAProblemSaysAboutItselfTest()
    {
        // set-info means nothing to a solver, and may hold anything: it does not reach one.
        var problem = "(set-info :source |anything, caf" + (char)0xE9 + "|)(set-logic QF_LIA)(set-info :status sat)(declare-const x Int)(set-info :note \"between\")(assert (> x 0))";

        var script = SmtClosedScript.Build(SmtCheck.Commands(problem), SmtCheck.Model(null, "((x 1))"));

        Assert.That(Lines(script), Is.EqualTo(new[] { "(set-logic QF_LIA)", "(define-fun x () Int 1)", "(assert (> x 0))", "(check-sat)" }));
    }

    [Test]
    public void BuildMatchesTheNamesOfTheModelToTheProblemsByTheNameAloneTest()
    {
        // The problem declares the names between bars, the solver prints them bare: the same
        // names. The definition keeps the problem's spelling.
        const string problem = "(declare-const |assert| Int)(declare-fun |reset| (Int) Int)(assert (= (|reset| |assert|) 5))";
        const string model = "((define-fun assert () Int 1) (define-fun reset ((x!0 Int)) Int (ite (= x!0 assert) 5 0)))";

        var script = Build(problem, model);

        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(define-fun |assert| () Int 1)",
            "(define-fun |reset| ((x!0 Int)) Int (ite (= x!0 assert) 5 0))",
            "(assert (= (|reset| |assert|) 5))",
            "(check-sat)"
        }));
        Assert.That(script.Free, Is.EqualTo(0));
        Assert.That(SmtCheck.Validate(problem, model).Class, Is.EqualTo(OutWit.Controller.AI.Smt.Model.SmtValidationClass.Validated));
    }

    #endregion

    #region Edge Tests

    [Test]
    public void BuildKeepsNamesAsTheyWereWrittenTest()
    {
        const string problem = "(declare-const |a b| Int)(declare-const |let| Int)(assert (! (> |a b| |let|) :named |n 1|))";
        const string model = "((define-fun |a b| () Int 2) (define-fun |let| () Int 1))";

        var script = Build(problem, model);

        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(define-fun |a b| () Int 2)",
            "(define-fun |let| () Int 1)",
            "(assert (! (> |a b| |let|) :named |n 1|))",
            "(check-sat)"
        }));
    }

    [Test]
    public void BuildDefinesNothingFromAnUnreadableModelTest()
    {
        var script = Build("(declare-const x Int)(declare-fun f (Int) Int)(assert (= (f x) 1))", "(define-fun x () Int 1)");

        Assert.That(script.Defined, Is.EqualTo(0));
        Assert.That(script.Free, Is.EqualTo(2));
        Assert.That(Lines(script), Has.Length.EqualTo(4));
        Assert.That(Lines(script)[^1], Is.EqualTo("(check-sat)"));
    }

    [Test]
    public void BuildLeavesWhatTheProblemDefinesToTheProblemTest()
    {
        const string problem = "(define-fun c () Int 5)(declare-const x Int)(declare-const x Int)(assert (= x c))";
        const string model = "((define-fun c () Int 9) (define-fun x () Int 5))";

        var script = Build(problem, model);

        // The model's c is not the problem's; a name the problem declares twice is defined once.
        Assert.That(Lines(script), Is.EqualTo(new[]
        {
            "(define-fun x () Int 5)",
            "(define-fun c () Int 5)",
            "(declare-const x Int)",
            "(assert (= x c))",
            "(check-sat)"
        }));
    }

    [Test]
    public void BuildEndsForDefinitionsThatMentionEachOtherInACircleTest()
    {
        const string model = "((define-fun x () Int (+ y 1)) (define-fun y () Int (+ x 1)) (define-fun k!0 ((i Int)) Int (k!0 i)))";

        var script = Build("(declare-const x Int)(declare-const y Int)(assert (= x y))", model);

        Assert.That(script.Commands.Count(command => command.Head == "define-fun"), Is.EqualTo(3));
        Assert.That(script.Defined, Is.EqualTo(2));
    }

    [Test]
    public void BuildTakesCommandsItCannotReadAsTheyAreTest()
    {
        var script = SmtClosedScript.Build(SmtCheck.Commands("(declare-fun f)(declare-const 5 Int)(assert)(weird 1 2)x"), SmtCheck.Model("((define-fun f () Int 1))"));

        Assert.That(Lines(script), Is.EqualTo(new[] { "(define-fun f () Int 1)", "(declare-fun f)", "(declare-const 5 Int)", "(assert)", "(weird 1 2)", "x", "(check-sat)" }));
        Assert.Throws<ArgumentNullException>(() => SmtClosedScript.Build(null!, SmtCheck.Model(null)));
        Assert.Throws<ArgumentNullException>(() => SmtClosedScript.Build([], null!));
    }

    #endregion

    #region Fixture Tests

    [Test]
    public void BuildClosesEveryFixtureTest()
    {
        foreach (var fixture in SmtFixtures.All)
        {
            var commands = SmtCheck.Commands(fixture.Problem);
            var validator = new SmtModelValidator(commands);

            var script = SmtClosedScript.Build(commands, SmtCheck.Model(fixture.Model, fixture.Values));
            var text = script.ToString();
            var read = SmtReader.Read(text, SmtReaderLimits.Problem);

            // Every declared symbol became a definition, the text reads back as the same
            // commands, and nothing is declared any more but sorts and universe elements.
            Assert.That(script.Defined, Is.EqualTo(validator.Declared), fixture.Name);
            Assert.That(script.Free, Is.EqualTo(0), fixture.Name);
            Assert.That(read, Has.Count.EqualTo(script.Commands.Count), fixture.Name);
            Assert.That(read.Zip(script.Commands).All(pair => pair.First.Is(pair.Second)), Is.True, fixture.Name);
            Assert.That(read[^1].ToString(), Is.EqualTo("(check-sat)"));
            Assert.That(read.Count(command => command.Head == "declare-const"), Is.EqualTo(0), fixture.Name);
            Assert.That(read.Where(command => command.Head == "declare-fun").All(command => command.Items[2].Items.Count == 0), Is.True, fixture.Name);
            Assert.That(read.Count(command => command.Head == "assert"), Is.EqualTo(validator.Assertions), fixture.Name);
        }
    }

    [Test]
    public void BuildDefinesBeforeUseInEveryFixtureTest()
    {
        foreach (var fixture in SmtFixtures.All)
        {
            var script = SmtClosedScript.Build(SmtCheck.Commands(fixture.Problem), SmtCheck.Model(fixture.Model, fixture.Values));
            var defined = script.Commands.Where(command => command.Head == "define-fun").Select(command => command.Items[1].Text).ToHashSet();
            var seen = new HashSet<string>();
            foreach (var command in script.Commands)
            {
                if (command.Head is "define-fun" or "declare-fun")
                    seen.Add(command.Items[1].Text);

                if (command.Head != "define-fun")
                    continue;

                // Everything the body names among the script's definitions is already defined.
                var pending = new Stack<SmtExpression>();
                pending.Push(command.Items[4]);
                while (pending.Count > 0)
                {
                    var term = pending.Pop();
                    if (term.Kind == SmtExpressionKind.Symbol && defined.Contains(term.Text))
                        Assert.That(seen, Does.Contain(term.Text), $"{fixture.Name}: {command.Items[1].Text} uses {term.Text}");

                    foreach (var item in term.Items)
                        pending.Push(item);
                }
            }
        }
    }

    #endregion

    #region Tools

    private static SmtClosedScript Build(string problem, string? model)
    {
        return SmtClosedScript.Build(SmtCheck.Commands(problem), SmtCheck.Model(model));
    }

    private static string[] Lines(SmtClosedScript script)
    {
        return script.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    #endregion
}
