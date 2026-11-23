using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The problems the solver's outcomes are pinned on: every query, the logics the model evaluator
/// covers, and the ways a session ends that cost little (an error of the solver's, a refusal,
/// each kind of <c>unknown</c> that a small budget reaches). All of them together run in a few
/// seconds, so they can be run again and again: twice in a row, with explicit trap checks, on
/// eight threads. Written for these tests; none is a benchmark file.
/// </summary>
internal static class SmtSolverCases
{
    #region Constants

    /// <summary>Linear integers: a system with one solution among small numbers, with <c>mod</c> and <c>distinct</c>.</summary>
    public const string LIA = """
        (set-logic QF_LIA)
        (set-info :status sat)
        (declare-const x Int)
        (declare-const y Int)
        (declare-const z Int)
        (assert (= (+ x (* 2 y) (* 3 z)) 17))
        (assert (> x y))
        (assert (> y z))
        (assert (>= z (- 2)))
        (assert (= (mod x 3) 1))
        (assert (distinct x y z))
        """;

    /// <summary>Linear reals: values that are fractions.</summary>
    public const string LRA = """
        (set-logic QF_LRA)
        (declare-const r Real)
        (declare-const s Real)
        (declare-const t Real)
        (assert (= (* 3.0 r) 1.0))
        (assert (= (+ r s) (/ 5 6)))
        (assert (< t (- r)))
        (assert (> (+ t s) 0.125))
        """;

    /// <summary>Bit-vectors: the factors of a product, which the solver has to search for.</summary>
    public const string BV_FACTORS = """
        (set-logic QF_BV)
        (declare-const a (_ BitVec 32))
        (declare-const b (_ BitVec 32))
        (assert (= (bvmul a b) #x0001fffe))
        (assert (bvugt a #x00000001))
        (assert (bvugt b #x00000001))
        """;

    /// <summary>Bit-vectors: arithmetic, masks, shifts, extension and concatenation of bytes.</summary>
    public const string BV = """
        (set-logic QF_BV)
        (declare-const p (_ BitVec 8))
        (declare-const q (_ BitVec 8))
        (assert (= (bvadd p q) #xff))
        (assert (= (bvand p #x0f) #x0a))
        (assert (bvult q p))
        (assert (= ((_ zero_extend 8) p) (concat #x00 p)))
        (assert (not (= (bvlshr p #x01) (bvashr p #x01))))
        """;

    /// <summary>Uninterpreted functions over an uninterpreted sort: a cycle of three elements.</summary>
    public const string UF = """
        (set-logic QF_UF)
        (declare-sort U 0)
        (declare-fun f (U) U)
        (declare-fun g (U U) Bool)
        (declare-const a U)
        (declare-const b U)
        (declare-const c U)
        (assert (= (f a) b))
        (assert (= (f b) c))
        (assert (distinct a b c))
        (assert (g a b))
        (assert (not (g b a)))
        (assert (= (f (f (f a))) a))
        """;

    /// <summary>Arrays over uninterpreted sorts.</summary>
    public const string AX = """
        (set-logic QF_AX)
        (declare-sort Index 0)
        (declare-sort Element 0)
        (declare-const m (Array Index Element))
        (declare-const i Index)
        (declare-const j Index)
        (declare-const e Element)
        (declare-const d Element)
        (assert (distinct i j))
        (assert (distinct e d))
        (assert (= (select (store m i e) j) d))
        (assert (= (select m i) d))
        """;

    /// <summary>Arrays of integers.</summary>
    public const string ALIA = """
        (set-logic QF_ALIA)
        (declare-const a (Array Int Int))
        (declare-const k Int)
        (assert (= (select a 0) 5))
        (assert (= (select (store a 1 7) k) 7))
        (assert (> (select a k) (select a 0)))
        (assert (= (+ (select a 2) (select a 3)) 10))
        """;

    /// <summary>Datatypes: an enumeration and a list of it.</summary>
    public const string DT = """
        (set-logic QF_DT)
        (declare-datatypes ((Color 0) (Colors 0)) (((red) (green) (blue)) ((none) (more (first Color) (rest Colors)))))
        (declare-const l Colors)
        (declare-const c Color)
        (assert ((_ is more) l))
        (assert (= (first l) blue))
        (assert ((_ is more) (rest l)))
        (assert (= (first (rest l)) c))
        (assert (distinct c blue))
        (assert (= (rest (rest l)) none))
        """;

    /// <summary>Uninterpreted functions over integers.</summary>
    public const string UFLIA = """
        (set-logic QF_UFLIA)
        (declare-fun f (Int) Int)
        (declare-const x Int)
        (declare-const y Int)
        (assert (= (f x) (+ y 1)))
        (assert (= (f y) (- x 1)))
        (assert (> x y))
        (assert (> (f 0) (f 1)))
        """;

    /// <summary>Strings, with the basic operators only.</summary>
    public const string STRINGS = """
        (set-logic QF_SLIA)
        (declare-const s String)
        (declare-const t String)
        (assert (= (str.++ s t) "hello world"))
        (assert (= (str.len s) 5))
        (assert (str.prefixof "hel" s))
        (assert (= (str.at t 1) "w"))
        (assert (= (str.indexof t "o" 0) 2))
        """;

    /// <summary>Two integers with one solution: 7 and 3.</summary>
    public const string ONE_SOLUTION = """
        (set-logic QF_LIA)
        (declare-const x Int)
        (declare-const y Int)
        (assert (= (+ x y) 10))
        (assert (= (- x y) 4))
        """;

    /// <summary>Two integers with no solution: their sum and their difference have different parities.</summary>
    public const string NO_SOLUTION = """
        (set-logic QF_LIA)
        (declare-const x Int)
        (declare-const y Int)
        (assert (= (+ x y) 3))
        (assert (= (- x y) 2))
        """;

    /// <summary>A bit-vector of two bits and a flag: four combinations satisfy it.</summary>
    public const string FLAGS = """
        (set-logic QF_BV)
        (declare-const v (_ BitVec 2))
        (declare-const flag Bool)
        (assert (=> flag (= v #b11)))
        (assert (not (= v #b00)))
        """;

    /// <summary>A hypothesis of the proofs over integers.</summary>
    public const string ABOVE_ONE = """
        (set-logic QF_NIA)
        (declare-const x Int)
        (assert (> x 1))
        """;

    /// <summary>The declarations of the proof over bit-vectors.</summary>
    public const string TWO_BYTES = """
        (set-logic QF_BV)
        (declare-const x (_ BitVec 8))
        (declare-const y (_ BitVec 8))
        """;

    /// <summary>The declarations of the proof over arrays.</summary>
    public const string AN_ARRAY = """
        (set-logic QF_ALIA)
        (declare-const a (Array Int Int))
        (declare-const i Int)
        (declare-const v Int)
        """;

    /// <summary>A hypothesis of the proof over uninterpreted functions: two arguments with one value.</summary>
    public const string SAME_VALUE = """
        (set-logic QF_UF)
        (declare-sort U 0)
        (declare-fun f (U) U)
        (declare-const x U)
        (declare-const y U)
        (assert (= (f x) (f y)))
        """;

    /// <summary>A name the problem does not declare: the solver reports it.</summary>
    public const string UNDECLARED = """
        (set-logic QF_LIA)
        (declare-const x Int)
        (assert (> y 1))
        (assert (> x 1))
        """;

    /// <summary>A product of two constants in a logic of linear arithmetic: the solver refuses the assertion.</summary>
    public const string NONLINEAR_IN_LINEAR = """
        (set-logic QF_LIA)
        (declare-const x Int)
        (declare-const y Int)
        (assert (= (* x y) 6))
        """;

    /// <summary>An operator of strings the solver's procedure is not complete for: it answers unknown, by its own word.</summary>
    public const string INCOMPLETE = """
        (declare-const s String)
        (declare-const t String)
        (assert (= (str.replace_all s "a" t) "xyz"))
        (assert (str.contains s "a"))
        (assert (not (= t "")))
        """;

    /// <summary>The resource limit of the pigeonhole case: far too little for it.</summary>
    public const long SMALL_RESOURCE_LIMIT = 50_000;

    private static readonly Lazy<IReadOnlyList<SmtSolverCase>> ALL = new(Build);

    #endregion

    #region Functions

    /// <summary>Finds a case.</summary>
    /// <param name="id">The case's name.</param>
    /// <returns>The case.</returns>
    public static SmtSolverCase Of(string id)
    {
        return ALL.Value.Single(one => one.Id == id);
    }

    private static IReadOnlyList<SmtSolverCase> Build()
    {
        var plain = new SmtOptionsData();
        var cases = new List<SmtSolverCase>();

        void Check(string id, string text, string expected, SmtOptionsData? options = null, params string[] values)
        {
            cases.Add(new SmtSolverCase(id, text, SmtSolverProblems.Check(id, text, values), options ?? plain, expected));
        }

        void Enumerate(string id, string text, int maxModels, string expected, params string[] project)
        {
            cases.Add(new SmtSolverCase(id, text, SmtSolverProblems.Enumerate(id, text, maxModels, project), plain, expected));
        }

        void Prove(string id, string text, string goal, string expected)
        {
            cases.Add(new SmtSolverCase(id, text, SmtSolverProblems.Prove(id, text, goal), plain, expected));
        }

        // check: sat, a problem a logic.
        Check("check-lia-sat", LIA, "ok sat");
        Check("check-lra-sat", LRA, "ok sat");
        Check("check-bv-sat", BV, "ok sat");
        Check("check-bv-factors-sat", BV_FACTORS, "ok sat");
        Check("check-uf-sat", UF, "ok sat");
        Check("check-ax-sat", AX, "ok sat");
        Check("check-alia-sat", ALIA, "ok sat");
        Check("check-dt-sat", DT, "ok sat");
        Check("check-uflia-sat", UFLIA, "ok sat");
        Check("check-strings-sat", STRINGS, "ok sat");
        Check("check-empty-sat", "(set-logic QF_UF)", "ok sat");

        // check: unsat.
        Check("check-lia-unsat", NO_SOLUTION, "ok unsat");
        Check("check-lra-unsat", "(set-logic QF_LRA)(declare-const x Real)(declare-const y Real)(declare-const z Real)(assert (> x y))(assert (> y z))(assert (> z x))", "ok unsat");
        Check("check-bv-unsat", TWO_BYTES + "(assert (not (= (bvadd x y) (bvadd y x))))", "ok unsat");
        Check("check-uf-unsat", SAME_VALUE + "(assert (= x y))(assert (not (= (f (f x)) (f (f y)))))", "ok unsat");
        Check("check-ax-unsat", AN_ARRAY + "(assert (not (= (select (store a i v) i) v)))", "ok unsat");
        Check("check-dt-unsat", "(set-logic QF_DT)(declare-datatypes ((Pair 0)) (((pair (left Bool) (right Bool)))))(declare-const p Pair)(assert (not (= p (pair (left p) (right p)))))", "ok unsat");

        // check: unknown, each by its reason.
        Check("check-pigeonhole-rlimit", SmtSolverProblems.Pigeonhole(9), "ok unknown rlimit", new SmtOptionsData { ResourceLimit = SMALL_RESOURCE_LIMIT });
        Check("check-strings-incomplete", INCOMPLETE, "ok unknown incomplete");
        Check("check-memory-at-once", ONE_SOLUTION, "ok unknown memout", new SmtOptionsData { MemoryMegabytes = 4 });

        // check: the values of terms, another seed, an error of the solver's.
        Check("check-lia-values", LIA, "ok sat", null, "x", "(+ x y)", "(* 2 z)");
        Check("check-bv-values", BV, "ok sat", null, "(bvadd p q)", "((_ extract 3 0) p)");
        Check("check-bv-factors-seed-7", BV_FACTORS, "ok sat", new SmtOptionsData { Seed = 7 });
        Check("check-undeclared", UNDECLARED, "input_error");
        Check("check-nonlinear-in-linear", NONLINEAR_IN_LINEAR, "input_error");
        Check("check-value-undeclared", ONE_SOLUTION, "input_error", null, "(+ x w)");

        // enumerate.
        Enumerate("enumerate-queens-6", SmtSolverProblems.Queens(6), 100, "ok sat 4 complete", SmtSolverProblems.QueenNames(6));
        Enumerate("enumerate-queens-6-two", SmtSolverProblems.Queens(6), 2, "ok sat 2 limit", SmtSolverProblems.QueenNames(6));
        Enumerate("enumerate-queens-5-first", SmtSolverProblems.Queens(5), 100, "ok sat 5 complete", "q0");
        Enumerate("enumerate-one", ONE_SOLUTION, 0, "ok sat 1 complete", "x", "y");
        Enumerate("enumerate-none", NO_SOLUTION, 0, "ok unsat 0 complete", "x", "y");
        Enumerate("enumerate-flags", FLAGS, 100, "ok sat 4 complete", "v", "flag");
        Enumerate("enumerate-colors", DT, 100, "ok sat 2 complete", "c");
        Enumerate("enumerate-default-two", FLAGS, 0, "ok sat 2 limit", "v", "flag");

        // An enumeration whose second check is too hard for the resource limit: one model, and no word on a second.
        var guarded = SmtSolverProblems.Guarded(SmtSolverProblems.Pigeonhole(9), "easy");
        cases.Add(new SmtSolverCase("enumerate-then-rlimit", guarded, SmtSolverProblems.Enumerate("enumerate-then-rlimit", guarded, 100, "easy"),
            new SmtOptionsData { ResourceLimit = SMALL_RESOURCE_LIMIT }, "ok sat rlimit 1 incomplete"));

        // prove.
        Prove("prove-valid", ABOVE_ONE, "(> (* x x) x)", "ok unsat proved");
        Prove("prove-invalid", ABOVE_ONE, "(> x 2)", "ok sat counterexample");
        Prove("prove-bv-valid", TWO_BYTES, "(= (bvnot (bvand x y)) (bvor (bvnot x) (bvnot y)))", "ok unsat proved");
        Prove("prove-array-valid", AN_ARRAY, "(= (select (store a i v) i) v)", "ok unsat proved");
        Prove("prove-uf-invalid", SAME_VALUE, "(= x y)", "ok sat counterexample");

        // A problem that is not what it was said to be: no solver runs.
        var forged = SmtSolverProblems.Check("refused-hash", LIA);
        forged.Hash = new string('0', 64);
        cases.Add(new SmtSolverCase("refused-hash", LIA, forged, plain, "refused hash_mismatch"));

        return cases;
    }

    #endregion

    #region Properties

    /// <summary>Every case, in the order of the golden file.</summary>
    public static IReadOnlyList<SmtSolverCase> All => ALL.Value;

    /// <summary>The names of the cases, for a test that takes one a case.</summary>
    public static IEnumerable<string> Ids => ALL.Value.Select(one => one.Id);

    #endregion
}
