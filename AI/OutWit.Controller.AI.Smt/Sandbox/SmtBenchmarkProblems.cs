using System.Globalization;
using System.Text;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// The reference set of the node benchmark: thirty small problems written for it, the same on
/// every node, each with the outcome it must come to and the fuel it must cost. Every query is in
/// it, and the theories a run meets most (linear integers and reals, bit-vectors, uninterpreted
/// functions, arrays, datatypes, strings), each problem tens of milliseconds of a session. A
/// session's outcome and fuel are the same on every machine, so a node that computes otherwise
/// cannot score.
/// <para>
/// A change to a problem, to the solver module or to what a session sends moves the fuel: the set
/// is then another one, and <see cref="SmtBenchmark.UNIT"/> moves with it.
/// </para>
/// </summary>
internal static class SmtBenchmarkProblems
{
    #region Constants

    private const string LIA = "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(declare-const z Int)"
                               + "(assert (= (+ x (* 2 y) (* 3 z)) {0}))(assert (> x y))(assert (> y z))(assert (>= z (- 2)))"
                               + "(assert (= (mod x 3) {1}))(assert (distinct x y z))";

    private const string FACTORS = "(set-logic QF_BV)(declare-const a (_ BitVec 32))(declare-const b (_ BitVec 32))"
                                   + "(assert (= (bvmul a b) {0}))(assert (bvugt a #x00000001))(assert (bvugt b #x00000001))"
                                   + "(assert (bvult a #x00010000))(assert (bvult b #x00010000))";

    private const string LRA = "(set-logic QF_LRA)(declare-const r Real)(declare-const s Real)(declare-const t Real)"
                               + "(assert (= (* {0}.0 r) 1.0))(assert (= (+ r s) (/ 5 6)))(assert (< t (- r)))(assert (> (+ t s) 0.125))";

    private const string UF = "(set-logic QF_UF)(declare-sort U 0)(declare-fun f (U) U)(declare-const a U)(declare-const b U)(declare-const c U)"
                              + "(assert (= (f a) b))(assert (= (f b) c))(assert (distinct a b c))(assert (= (f (f (f a))) {0}))";

    private const string ARRAYS = "(set-logic QF_ALIA)(declare-const a (Array Int Int))(declare-const k Int)"
                                  + "(assert (= (select a 0) {0}))(assert (= (select (store a 1 7) k) 7))(assert (> (select a k) (select a 0)))"
                                  + "(assert (= (+ (select a 2) (select a 3)) 10))";

    private const string DATATYPES = "(set-logic QF_DT)(declare-datatypes ((Color 0) (Colors 0)) (((red) (green) (blue)) ((none) (more (first Color) (rest Colors)))))"
                                     + "(declare-const l Colors)(declare-const c Color)(assert ((_ is more) l))(assert (= (first l) {0}))"
                                     + "(assert ((_ is more) (rest l)))(assert (= (first (rest l)) c))(assert (distinct c {0}))(assert (= (rest (rest l)) none))";

    private const string STRINGS = "(set-logic QF_SLIA)(declare-const s String)(declare-const t String)"
                                   + "(assert (= (str.++ s t) \"{0}\"))(assert (= (str.len s) {1}))(assert (str.prefixof \"{2}\" s))";

    private const string TWO_BYTES = "(set-logic QF_BV)(declare-const x (_ BitVec 8))(declare-const y (_ BitVec 8))";

    private const string ABOVE_ONE = "(set-logic QF_NIA)(declare-const x Int)(assert (> x 1))";

    private static readonly Lazy<IReadOnlyList<SmtBenchmarkProblem>> ALL = new(Build);

    #endregion

    #region Functions

    private static IReadOnlyList<SmtBenchmarkProblem> Build()
    {
        var problems = new List<SmtBenchmarkProblem>();

        void Check(string id, string text, string expected, long fuel)
        {
            var problem = Taken(text);
            problems.Add(new SmtBenchmarkProblem(id, Data(problems.Count, id, SmtQuery.Check, problem), expected, fuel));
        }

        void Enumerate(string id, string text, int models, string[] project, string expected, long fuel)
        {
            var data = Data(problems.Count, id, SmtQuery.Enumerate, Taken(text));
            data.Project = project.Select(Term).ToList();
            data.MaxModels = models;
            problems.Add(new SmtBenchmarkProblem(id, data, expected, fuel));
        }

        void Prove(string id, string text, string goal, string expected, long fuel)
        {
            var data = Data(problems.Count, id, SmtQuery.Prove, Taken(text));
            data.Goal = Term(goal);
            problems.Add(new SmtBenchmarkProblem(id, data, expected, fuel));
        }

        Check("lia-17", Format(LIA, 17, 1), "ok sat", 25_905_395);
        Check("lia-23", Format(LIA, 23, 2), "ok sat", 25_745_881);
        Check("lia-31", Format(LIA, 31, 1), "ok sat", 25_754_113);
        Check("lia-40", Format(LIA, 40, 0), "ok sat", 26_188_940);
        Check("factors-1fffe", Format(FACTORS, "#x0001fffe"), "ok sat", 286_386_780);
        Check("factors-f4240", Format(FACTORS, "#x000f4240"), "ok sat", 832_825_811);
        Check("factors-3d0900", Format(FACTORS, "#x003d0900"), "ok sat", 318_278_434);
        Check("lra-3", Format(LRA, 3), "ok sat", 13_102_353);
        Check("lra-4", Format(LRA, 4), "ok sat", 13_101_403);
        Check("uf-a", Format(UF, "a"), "ok sat", 14_001_476);
        Check("uf-b", Format(UF, "b"), "ok sat", 13_820_237);
        Check("arrays-5", Format(ARRAYS, 5), "ok sat", 17_772_023);
        Check("arrays-6", Format(ARRAYS, 6), "ok sat", 17_806_210);
        Check("datatypes-blue", Format(DATATYPES, "blue"), "ok sat", 14_505_933);
        Check("datatypes-red", Format(DATATYPES, "red"), "ok sat", 14_504_733);
        Check("strings-hello", Format(STRINGS, "hello world", 5, "hel"), "ok sat", 36_649_273);
        Check("strings-smt", Format(STRINGS, "satisfiable", 3, "sat"), "ok sat", 27_945_134);
        Check("pigeons-4", Pigeonhole(4), "ok unsat", 17_266_666);
        Check("pigeons-5", Pigeonhole(5), "ok unsat", 24_661_570);
        Check("pigeons-6", Pigeonhole(6), "ok unsat", 67_599_720);
        Check("lia-parity", "(set-logic QF_LIA)(declare-const x Int)(declare-const y Int)(assert (= (+ x y) 3))(assert (= (- x y) 2))", "ok unsat", 21_474_243);

        Enumerate("queens-4", Queens(4), 100, QueenNames(4), "ok sat 2 complete", 39_169_521);
        Enumerate("queens-5", Queens(5), 100, QueenNames(5), "ok sat 10 complete", 78_157_416);
        Enumerate("queens-6", Queens(6), 100, QueenNames(6), "ok sat 4 complete", 100_781_887);
        Enumerate("flags", "(set-logic QF_BV)(declare-const v (_ BitVec 2))(declare-const flag Bool)(assert (=> flag (= v #b11)))(assert (not (= v #b00)))", 100, ["v", "flag"], "ok sat 4 complete", 22_984_714);

        Prove("bv-de-morgan", TWO_BYTES, "(= (bvnot (bvand x y)) (bvor (bvnot x) (bvnot y)))", "ok unsat proved", 12_083_298);
        Prove("bv-commutes", TWO_BYTES, "(= (bvadd x y) (bvadd y x))", "ok unsat proved", 12_036_216);
        Prove("bv-distributes", TWO_BYTES, "(= (bvmul x (bvadd y #x01)) (bvadd (bvmul x y) x))", "ok unsat proved", 12_343_686);
        Prove("square-grows", ABOVE_ONE, "(> (* x x) x)", "ok unsat proved", 30_737_289);
        Prove("above-two", ABOVE_ONE, "(> x 2)", "ok sat counterexample", 13_292_831);

        return problems;
    }

    private static SmtProblemData Data(int index, string id, SmtQuery query, SmtProblem problem)
    {
        return new SmtProblemData
        {
            Index = index,
            Id = id,
            Query = query,
            Text = problem.Canonical,
            Hash = problem.Hash,
            Expected = query == SmtQuery.Prove ? SmtProblemStatus.None : problem.Status
        };
    }

    private static SmtProblem Taken(string text)
    {
        var result = SmtSanitizer.Sanitize(text);
        return result.Problem ?? throw new InvalidOperationException($"a problem of the benchmark is refused: {result.Refusal?.Code}");
    }

    private static string Term(string text)
    {
        var result = SmtSanitizer.SanitizeTerm(text);
        return result.Term?.ToString() ?? throw new InvalidOperationException($"a term of the benchmark is refused: {result.Refusal?.Code}");
    }

    private static string Format(string template, params object[] values)
    {
        return string.Format(CultureInfo.InvariantCulture, template, values);
    }

    private static string Queens(int queens)
    {
        var builder = new StringBuilder("(set-logic QF_LIA)");
        for (var row = 0; row < queens; row++)
            builder.Append(CultureInfo.InvariantCulture, $"(declare-const q{row} Int)(assert (and (>= q{row} 0) (< q{row} {queens})))");

        builder.Append("(assert (distinct").Append(string.Concat(Enumerable.Range(0, queens).Select(row => $" q{row}"))).Append("))");
        for (var row = 0; row < queens; row++)
        {
            for (var other = row + 1; other < queens; other++)
                builder.Append(CultureInfo.InvariantCulture, $"(assert (and (not (= (- q{row} q{other}) {other - row})) (not (= (- q{other} q{row}) {other - row}))))");
        }

        return builder.ToString();
    }

    private static string[] QueenNames(int queens)
    {
        return Enumerable.Range(0, queens).Select(row => $"q{row}").ToArray();
    }

    private static string Pigeonhole(int holes)
    {
        var builder = new StringBuilder("(set-logic QF_UF)");
        for (var pigeon = 0; pigeon <= holes; pigeon++)
        {
            for (var hole = 0; hole < holes; hole++)
                builder.Append(CultureInfo.InvariantCulture, $"(declare-const p{pigeon}_{hole} Bool)");
        }

        for (var pigeon = 0; pigeon <= holes; pigeon++)
            builder.Append("(assert (or").Append(string.Concat(Enumerable.Range(0, holes).Select(hole => $" p{pigeon}_{hole}"))).Append("))");

        for (var hole = 0; hole < holes; hole++)
        {
            for (var pigeon = 0; pigeon <= holes; pigeon++)
            {
                for (var other = pigeon + 1; other <= holes; other++)
                    builder.Append(CultureInfo.InvariantCulture, $"(assert (not (and p{pigeon}_{hole} p{other}_{hole})))");
            }
        }

        return builder.ToString();
    }

    #endregion

    #region Properties

    /// <summary>The set, in a fixed order; a problem's index is its place in it.</summary>
    public static IReadOnlyList<SmtBenchmarkProblem> All => ALL.Value;

    #endregion
}
