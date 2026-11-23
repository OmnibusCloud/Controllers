using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Makes the problems of the solver's tests as they travel to a node (the sanitizer's canonical
/// text and its hash, the terms beside it as canonical text), generates the ones that are
/// written by a rule, and checks a model the solver printed against a problem with the model
/// evaluator. Every problem here is written for these tests: none is a benchmark file.
/// </summary>
internal static class SmtSolverProblems
{
    #region Constants

    private const char LINE_FEED = '\n';

    #endregion

    #region Functions

    /// <summary>
    /// A problem with the query <c>check</c>.
    /// </summary>
    /// <param name="id">The problem's id.</param>
    /// <param name="text">The problem's text.</param>
    /// <param name="values">The terms whose values are asked, or none.</param>
    /// <returns>The problem as it travels.</returns>
    public static SmtProblemData Check(string id, string text, params string[] values)
    {
        var problem = Taken(text);
        return new SmtProblemData
        {
            Id = id,
            Query = SmtQuery.Check,
            Text = problem.Canonical,
            Hash = problem.Hash,
            Values = values.Select(Term).ToList(),
            Expected = problem.Status
        };
    }

    /// <summary>
    /// A problem with the query <c>enumerate</c>.
    /// </summary>
    /// <param name="id">The problem's id.</param>
    /// <param name="text">The problem's text.</param>
    /// <param name="maxModels">The most models to look for; 0 for the number of the run's options.</param>
    /// <param name="project">The constants the models are told apart by.</param>
    /// <returns>The problem as it travels.</returns>
    public static SmtProblemData Enumerate(string id, string text, int maxModels, params string[] project)
    {
        var problem = Taken(text);
        return new SmtProblemData
        {
            Id = id,
            Query = SmtQuery.Enumerate,
            Text = problem.Canonical,
            Hash = problem.Hash,
            Project = project.Select(Term).ToList(),
            MaxModels = maxModels
        };
    }

    /// <summary>
    /// A problem with the query <c>prove</c>.
    /// </summary>
    /// <param name="id">The problem's id.</param>
    /// <param name="text">The problem's text: the hypotheses.</param>
    /// <param name="goal">The goal.</param>
    /// <returns>The problem as it travels.</returns>
    public static SmtProblemData Prove(string id, string text, string goal)
    {
        var problem = Taken(text);
        return new SmtProblemData
        {
            Id = id,
            Query = SmtQuery.Prove,
            Text = problem.Canonical,
            Hash = problem.Hash,
            Goal = Term(goal)
        };
    }

    /// <summary>
    /// Checks a model the solver printed against a problem's text with the model evaluator.
    /// </summary>
    /// <param name="text">The problem's text.</param>
    /// <param name="model">The answer of <c>(get-model)</c>, or null.</param>
    /// <param name="values">The answer of <c>(get-value (...))</c>, or null.</param>
    /// <returns>The validation.</returns>
    public static SmtValidation Validate(string text, string? model, string? values = null)
    {
        var validator = new SmtModelValidator(Taken(text));
        return validator.Validate(SmtModel.Read(model, values, SmtReaderLimits.Unbounded), SmtEvaluationBudget.Default);
    }

    /// <summary>
    /// The n-queens problem as integers, a queen a row: 92 solutions for eight, 4 for six, 1 for one, none for three.
    /// </summary>
    /// <param name="queens">The size of the board.</param>
    /// <returns>The problem's text.</returns>
    public static string Queens(int queens)
    {
        var builder = new StringBuilder("(set-logic QF_LIA)\n");
        for (var row = 0; row < queens; row++)
            builder.Append($"(declare-const q{row} Int)\n(assert (and (>= q{row} 0) (< q{row} {queens})))\n");

        if (queens > 1)
            builder.Append("(assert (distinct").Append(string.Concat(Enumerable.Range(0, queens).Select(row => $" q{row}"))).Append("))\n");

        for (var row = 0; row < queens; row++)
        {
            for (var other = row + 1; other < queens; other++)
                builder.Append($"(assert (and (not (= (- q{row} q{other}) {other - row})) (not (= (- q{other} q{row}) {other - row}))))\n");
        }

        return builder.ToString();
    }

    /// <summary>The names of the queens of <see cref="Queens"/>, in order.</summary>
    /// <param name="queens">The size of the board.</param>
    /// <returns>The constants.</returns>
    public static string[] QueenNames(int queens)
    {
        return Enumerable.Range(0, queens).Select(row => $"q{row}").ToArray();
    }

    /// <summary>
    /// The pigeonhole principle as Booleans, one pigeon more than there are holes: unsatisfiable,
    /// and exponentially hard for the solver, so a resource limit or a fuel budget ends it first.
    /// </summary>
    /// <param name="holes">The number of holes.</param>
    /// <returns>The problem's text.</returns>
    public static string Pigeonhole(int holes)
    {
        var builder = new StringBuilder("(set-logic QF_UF)\n");
        for (var pigeon = 0; pigeon <= holes; pigeon++)
        {
            for (var hole = 0; hole < holes; hole++)
                builder.Append($"(declare-const p{pigeon}_{hole} Bool)\n");
        }

        for (var pigeon = 0; pigeon <= holes; pigeon++)
            builder.Append("(assert (or").Append(string.Concat(Enumerable.Range(0, holes).Select(hole => $" p{pigeon}_{hole}"))).Append("))\n");

        for (var hole = 0; hole < holes; hole++)
        {
            for (var pigeon = 0; pigeon <= holes; pigeon++)
            {
                for (var other = pigeon + 1; other <= holes; other++)
                    builder.Append($"(assert (not (and p{pigeon}_{hole} p{other}_{hole})))\n");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// A problem behind a guard: every assertion holds when the guard is true, and the problem's
    /// own assertions must hold when it is false. With the guard true it is satisfied at once;
    /// with the guard ruled out it is as hard as the problem.
    /// </summary>
    /// <param name="text">The problem's text, an assertion a line, each <c>(assert ...)</c>.</param>
    /// <param name="guard">The name of the guard: a Boolean constant the problem does not declare.</param>
    /// <returns>The guarded problem's text.</returns>
    public static string Guarded(string text, string guard)
    {
        const string ASSERT = "(assert ";
        var builder = new StringBuilder();
        var declared = false;
        foreach (var line in text.Split(LINE_FEED, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith(ASSERT, StringComparison.Ordinal))
            {
                builder.Append(line).Append(LINE_FEED);
                continue;
            }

            if (!declared)
                builder.Append($"(declare-const {guard} Bool)").Append(LINE_FEED);

            declared = true;
            builder.Append($"(assert (or {guard} {line[ASSERT.Length..^1]}))").Append(LINE_FEED);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A multiplication of two bit-vectors of a width: the solver turns it into clauses whose
    /// number grows with the square of the width, and their memory with it.
    /// </summary>
    /// <param name="width">The width in bits, a multiple of four.</param>
    /// <returns>The problem's text.</returns>
    public static string WideMultiplication(int width)
    {
        var one = "#x" + new string('0', width / 4 - 1) + "1";
        var zero = "#x" + new string('0', width / 4);
        return $"(set-logic QF_BV)\n(declare-const a (_ BitVec {width}))\n(declare-const b (_ BitVec {width}))\n"
               + $"(assert (= (bvmul a b) (bvadd a {one})))\n(assert (distinct a {zero}))\n";
    }

    /// <summary>
    /// A problem with many constants that any values satisfy: its model is a line a constant.
    /// </summary>
    /// <param name="constants">The number of constants.</param>
    /// <returns>The problem's text.</returns>
    public static string ManyConstants(int constants)
    {
        var builder = new StringBuilder("(set-logic QF_LIA)\n");
        for (var index = 0; index < constants; index++)
            builder.Append($"(declare-const some_constant_with_a_long_name_{index} Int)\n(assert (> some_constant_with_a_long_name_{index} {index}))\n");

        return builder.ToString();
    }

    #endregion

    #region Tools

    private static SmtProblem Taken(string text)
    {
        var result = SmtSanitizer.Sanitize(text);
        if (result.Refusal is { } refusal)
            throw new InvalidOperationException($"a problem of the tests is refused: {refusal.Code}, line {refusal.Line}: {refusal.Reason}");

        return result.Problem!;
    }

    private static string Term(string text)
    {
        var result = SmtSanitizer.SanitizeTerm(text);
        if (result.Refusal is { } refusal)
            throw new InvalidOperationException($"a term of the tests is refused: {refusal.Code}: {refusal.Reason}");

        return result.Term!.ToString();
    }

    #endregion
}
