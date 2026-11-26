namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Enumerations projected on a constant of every kind of sort a problem can give one: the sorts
/// of the theories, alone and inside each other, the problem's own datatypes and aliases, and the
/// sorts a problem only declares. An enumeration rules a found model out by saying the projected
/// constants' values back to the solver, so each case is a walk of one sort through that: what
/// the pinned solver printed for it and whether it read it back was looked at case by case, and
/// the words here are what came of it.
/// <para>
/// The projections that are taken (<see cref="Taken"/>) enumerate, each to the words written
/// beside it. The ones that are refused (<see cref="Refused"/>) are exactly those whose values
/// hold an element of a sort the problem leaves to the solver (<c>declare-sort</c>): the solver
/// prints such an element as <c>U!val!0</c> and answers <c>unknown constant U!val!0</c> when it
/// is said back. Among the taken ones are the cases that tell the rule from a cruder one: a sort
/// that only names an uninterpreted sort where no value goes (a parameter no field uses, an
/// alias that drops its parameter) still enumerates.
/// </para>
/// <para>
/// Written for these tests; none is a benchmark file.
/// </para>
/// </summary>
internal static class SmtProjectionCases
{
    #region Constants

    private const string U = "(declare-sort U 0)";
    private const string LIST = "(declare-datatype Lst (par (T) ((nl) (cns (hd T) (tl (Lst T))))))";
    private const string OPTION = "(declare-datatype Opt (par (T) ((none) (some (get T)))))";
    private const string PAIR = "(declare-datatype Pr (par (A B) ((pr (fst A) (snd B)))))";
    private const string PHANTOM = "(declare-datatype Ph (par (T) ((ph (val Bool)))))";

    private static readonly Lazy<IReadOnlyList<SmtProjectionCase>> ALL = new(Build);

    #endregion

    #region Functions

    /// <summary>Finds a case.</summary>
    /// <param name="id">The case's name.</param>
    /// <returns>The case.</returns>
    public static SmtProjectionCase Of(string id)
    {
        return ALL.Value.Single(one => one.Id == id);
    }

    private static IReadOnlyList<SmtProjectionCase> Build()
    {
        var cases = new List<SmtProjectionCase>();

        void Take(string id, string text, int maxModels, string words, params string[] project)
        {
            cases.Add(new SmtProjectionCase(id, text, maxModels, project, words));
        }

        void Refuse(string id, string text, string sort, params string[] project)
        {
            cases.Add(new SmtProjectionCase(id, text, 3, project, "refused project_sort_unsupported") { Uninterpreted = sort });
        }

        // The core and the numbers.
        Take("bool", "(declare-const p Bool)", 5, "ok sat 2 complete", "p");
        Take("int", "(declare-const x Int)(assert (and (>= x (- 2)) (<= x 0)))", 5, "ok sat 3 complete", "x");
        Take("int-thirty-digits", "(declare-const x Int)(assert (or (= x 123456789012345678901234567890) (= x (- 123456789012345678901234567890))))", 5, "ok sat 2 complete", "x");
        Take("real-fractions", "(declare-const r Real)(assert (or (= r (/ 1 3)) (= r (- 2.5)) (= r 4.0)))", 5, "ok sat 3 complete", "r");
        Take("real-irrational", "(set-logic QF_NRA)(declare-const x Real)(assert (= (* x x) 2.0))", 5, "ok sat 2 complete", "x");
        Take("int-and-real", "(declare-const x Int)(declare-const r Real)(assert (and (>= x 0) (<= x 1)))(assert (= r (/ x 3)))", 5, "ok sat 2 complete", "x", "r");

        // Bit-vectors, strings, sequences, characters, regular expressions.
        Take("bitvec-3", "(declare-const v (_ BitVec 3))(assert (bvult v #b011))", 5, "ok sat 3 complete", "v");
        Take("bitvec-8", "(declare-const v (_ BitVec 8))(assert (bvugt v #xfd))", 5, "ok sat 2 complete", "v");
        Take("bitvec-65", "(declare-const v (_ BitVec 65))(assert (bvult v (_ bv2 65)))", 5, "ok sat 2 complete", "v");
        Take("string", "(declare-const s String)(assert (or (= s \"\") (= s \"a\\u{e9}b\") (= s \"q\"\"q\") (= s \"a b\\x\")))", 6, "ok sat 4 complete", "s");
        Take("string-escapes", "(declare-const s String)(assert (or (= s \"\\u{0}\") (= s \"\\u{7f}\") (= s \"\\u{d800}\")))", 6, "ok sat 3 complete", "s");
        Take("seq-int", "(declare-const q (Seq Int))(assert (<= (seq.len q) 1))(assert (=> (= (seq.len q) 1) (= (seq.nth q 0) 7)))", 5, "ok sat 2 complete", "q");
        Take("seq-of-three", "(declare-const q (Seq Int))(assert (= (seq.len q) 3))(assert (= (seq.nth q 0) (- 1)))(assert (= (seq.nth q 1) 2))(assert (or (= (seq.nth q 2) 3) (= (seq.nth q 2) 4)))", 5, "ok sat 2 complete", "q");
        Take("seq-bitvec", "(declare-const q (Seq (_ BitVec 4)))(assert (= (seq.len q) 1))(assert (bvult (seq.nth q 0) #x2))", 5, "ok sat 2 complete", "q");
        Take("seq-seq", "(declare-const q (Seq (Seq Int)))(assert (= (seq.len q) 1))(assert (<= (seq.len (seq.nth q 0)) 1))(assert (=> (= (seq.len (seq.nth q 0)) 1) (= (seq.nth (seq.nth q 0) 0) 5)))", 5, "ok sat 2 complete", "q");
        Take("seq-real", "(declare-const q (Seq Real))(assert (= (seq.len q) 1))(assert (or (= (seq.nth q 0) 0.5) (= (seq.nth q 0) (- 1.5))))", 5, "ok sat 2 complete", "q");
        Take("character", "(declare-const c Unicode)(assert (or (= c (_ Char 97)) (= c (_ Char 98))))", 5, "ok sat 2 complete", "c");
        Take("regular-expression", "(declare-const r RegLan)(assert (= r (re.* (str.to_re \"a\"))))", 3, "ok sat 1 complete", "r");

        // Floating point.
        Take("float-special", "(declare-const f (_ FloatingPoint 8 24))(assert (or (fp.isNaN f) (fp.isInfinite f) (fp.isZero f)))", 8, "ok sat 5 complete", "f");
        Take("float-32", "(declare-const f Float32)(assert (or (fp.eq f ((_ to_fp 8 24) RNE 1.5)) (fp.eq f ((_ to_fp 8 24) RNE (- 0.1)))))", 5, "ok sat 2 complete", "f");
        Take("float-64", "(declare-const f Float64)(assert (fp.eq f ((_ to_fp 11 53) RNE 3.25)))", 5, "ok sat 1 complete", "f");
        Take("rounding-mode", "(declare-const m RoundingMode)", 8, "ok sat 5 complete", "m");

        // Arrays: constant arrays, stores, shared stores, a value written as a function.
        Take("array-int-int", "(set-logic QF_ALIA)(declare-const a (Array Int Int))(assert (= (select a 0) 1))", 3, "ok sat 3 limit", "a");
        Take("array-bool-bool", "(declare-const a (Array Bool Bool))", 8, "ok sat 4 complete", "a");
        Take("array-bitvec-bool", "(set-logic QF_ABV)(declare-const a (Array (_ BitVec 2) Bool))", 20, "ok sat 16 complete", "a");
        Take("array-of-arrays", "(declare-const a (Array Int (Array Int Int)))(assert (= (select (select a 1) 2) 3))", 3, "ok sat 3 limit", "a");
        Take("array-by-array", "(declare-const a (Array (Array Int Int) Int))(declare-const k (Array Int Int))(assert (= (select a k) 3))(assert (= (select k 0) 1))", 3, "ok sat 3 limit", "a");
        Take("array-string", "(declare-const a (Array String Int))(assert (= (select a \"k\") 3))", 3, "ok sat 3 limit", "a");
        Take("array-real", "(declare-const a (Array Real Real))(assert (= (select a 0.5) (/ 1 3)))", 3, "ok sat 3 limit", "a");
        Take("array-two-indices", "(declare-const a (Array Int Int Int))(assert (= (select a 1 2) 3))", 3, "ok sat 3 limit", "a");
        Take("array-of-sequences", "(declare-const a (Array Int (Seq Int)))(assert (= (seq.len (select a 0)) 1))", 3, "ok sat 3 limit", "a");
        Take("sequence-of-arrays", "(declare-const q (Seq (Array Int Int)))(assert (= (seq.len q) 1))(assert (= (select (seq.nth q 0) 0) 1))", 3, "ok sat 3 limit", "q");
        Take("array-as-a-function", "(declare-const a (Array Int Bool))(assert (forall ((i Int)) (= (select a i) (> i 3))))", 3, "ok sat 1 complete", "a");
        Take("function-sort", "(declare-const h (-> Int Int))(assert (= (select h 0) 1))", 3, "ok sat 3 limit", "h");
        Take("set", "(declare-const s (Set Int))(assert (select s 1))", 3, "ok sat 3 limit", "s");

        // Datatypes and aliases.
        Take("datatype-enumeration", "(declare-datatype Color ((red) (green) (blue)))(declare-const c Color)", 5, "ok sat 3 complete", "c");
        Take("datatype-record", "(declare-datatype P ((mk (n Int) (b Bool))))(declare-const p P)(assert (and (>= (n p) 0) (<= (n p) 1)))", 6, "ok sat 4 complete", "p");
        Take("datatype-list", "(declare-datatype L ((nil) (cons (hd Int) (tl L))))(declare-const l L)(assert (or (= l nil) (= l (cons 1 nil)) (= l (cons (- 1) (cons 2 nil)))))", 5, "ok sat 3 complete", "l");
        Take("datatype-mutual", "(declare-datatypes ((T 0) (F 0)) (((leaf) (node (kids F))) ((none) (more (first T) (rest F)))))(declare-const t T)(assert (or (= t leaf) (= t (node none)) (= t (node (more leaf none)))))", 5, "ok sat 3 complete", "t");
        Take("datatype-parametric-list", LIST + "(declare-const l (Lst Int))(assert (or (= l (as nl (Lst Int))) (= l (cns 1 (as nl (Lst Int))))))", 5, "ok sat 2 complete", "l");
        Take("datatype-parametric-pair", PAIR + "(declare-const p (Pr Bool (_ BitVec 2)))(assert (bvult (snd p) #b10))", 6, "ok sat 4 complete", "p");
        Take("datatype-option-of-option", OPTION + "(declare-const o (Opt (Opt Bool)))", 6, "ok sat 4 complete", "o");
        Take("datatype-fields-of-theories", "(declare-datatype D ((mk (s String) (r Real) (v (_ BitVec 4)))))(declare-const d D)(assert (= (s d) \"x\"))(assert (= (r d) (/ 1 3)))(assert (bvult (v d) #x2))", 5, "ok sat 2 complete", "d");
        Take("datatype-array-field", "(declare-datatype D ((mk (arr (Array Int Int)) (n Int))))(declare-const d D)(assert (= (select (arr d) 0) (n d)))(assert (= (n d) 4))", 3, "ok sat 3 limit", "d");
        Take("datatype-one-constructor-name-in-two", "(declare-datatype A ((nil) (a1)))(declare-datatype B ((nil) (b1)))(declare-const x A)(declare-const y B)", 5, "ok sat 4 complete", "x", "y");
        Take("datatype-quoted-constructor", "(declare-datatype C ((|a b|) (c) (|d!val!0|)))(declare-const x C)", 5, "ok sat 3 complete", "x");
        Take("array-of-datatype", "(declare-datatype Color ((red) (green)))(declare-const a (Array Color Color))", 6, "ok sat 4 complete", "a");
        Take("alias", "(define-sort Word () (_ BitVec 2))(declare-const w Word)", 6, "ok sat 4 complete", "w");
        Take("alias-with-parameters", "(define-sort Map (K V) (Array K V))(declare-const m (Map Bool Bool))", 6, "ok sat 4 complete", "m");

        // An uninterpreted sort that is named where no value goes: these enumerate, and are taken.
        Take("uninterpreted-beside", U + "(declare-const x U)(declare-const n Int)(assert (and (>= n 0) (<= n 1)))", 5, "ok sat 2 complete", "n");
        Take("uninterpreted-for-an-unused-parameter", U + PHANTOM + "(declare-const p (Ph U))", 5, "ok sat 2 complete", "p");
        Take("uninterpreted-array-for-an-unused-parameter", U + PHANTOM + "(declare-const p (Ph (Array Int U)))", 5, "ok sat 2 complete", "p");
        Take("uninterpreted-with-arguments-for-an-unused-parameter", "(declare-sort P 1)" + PHANTOM + "(declare-const p (Ph (P Int)))", 5, "ok sat 2 complete", "p");
        Take("uninterpreted-for-a-parameter-an-alias-drops", U + "(define-sort K (T) Int)(declare-const x (K U))(assert (and (>= x 0) (<= x 1)))", 5, "ok sat 2 complete", "x");

        // The uninterpreted sort itself, however it is named.
        Refuse("uninterpreted", "(set-logic QF_UF)" + U + "(declare-const x U)(declare-const y U)(assert (distinct x y))", "U", "x");
        Refuse("uninterpreted-declare-fun", U + "(declare-fun x () U)(declare-fun y () U)(assert (distinct x y))", "U", "x");
        Refuse("uninterpreted-with-arguments", "(declare-sort P 1)(declare-const x (P Int))(declare-const y (P Int))(assert (distinct x y))", "P", "x");
        Refuse("uninterpreted-quoted-name", "(declare-sort |my sort| 0)(declare-const x |my sort|)(declare-const y |my sort|)(assert (distinct x y))", "my sort", "x");
        Refuse("uninterpreted-by-an-alias", U + "(define-sort V () U)(declare-const x V)(declare-const y V)(assert (distinct x y))", "U", "x");
        Refuse("uninterpreted-by-an-alias-parameter", U + "(define-sort Id (T) T)(declare-const x (Id U))(declare-const y (Id U))(assert (distinct x y))", "U", "x");
        Refuse("uninterpreted-of-one-element", U + "(declare-const x U)(assert (forall ((y U)) (= x y)))", "U", "x");

        // Sorts of the theories over it.
        Refuse("uninterpreted-array-index", U + "(declare-const a (Array U Int))(declare-const x U)(assert (= (select a x) 1))", "U", "a");
        Refuse("uninterpreted-array-element", U + "(declare-const a (Array Int U))", "U", "a");
        Refuse("uninterpreted-sequence", U + "(declare-const q (Seq U))(assert (= (seq.len q) 1))", "U", "q");
        Refuse("uninterpreted-array-of-sequences", U + "(declare-const a (Array Int (Seq U)))(assert (= (seq.len (select a 0)) 1))", "U", "a");
        Refuse("uninterpreted-function-sort", U + "(declare-const h (-> Int U))", "U", "h");
        Refuse("uninterpreted-set", U + "(declare-const s (Set U))(declare-const e U)(assert (select s e))", "U", "s");

        // Datatypes with a field of it, directly, by a parameter, by another datatype.
        Refuse("uninterpreted-field", U + "(declare-datatype D ((mk (u U) (n Int))))(declare-const d D)(assert (= (n d) 1))", "U", "d");
        Refuse("uninterpreted-option", U + OPTION + "(declare-const o (Opt U))", "U", "o");
        Refuse("uninterpreted-pair", U + PAIR + "(declare-const p (Pr U Int))(assert (= (snd p) 1))", "U", "p");
        Refuse("uninterpreted-list-of-records", U + "(declare-datatype D ((mk (u U))))" + LIST + "(declare-const l (Lst D))(assert (not (= l (as nl (Lst D)))))", "U", "l");
        Refuse("uninterpreted-mutual", U + "(declare-datatypes ((T 0) (F 0)) (((leaf) (node (kids F))) ((none) (more (first U) (rest F)))))(declare-const t T)(assert (not (= t leaf)))(assert (not (= (kids t) none)))", "U", "t");
        Refuse("uninterpreted-under-a-parameter-of-its-name", U + "(declare-datatype Box (par (U) ((box (get U)))))(declare-const b (Box Bool))", "U", "b");

        // The entry the refusal names is the one of the constant, wherever it stands.
        cases.Add(new SmtProjectionCase("uninterpreted-second-entry", U + "(declare-const n Int)(declare-const x U)(assert (and (>= n 0) (<= n 1)))", 3, ["n", "x"], "refused project_sort_unsupported") { Uninterpreted = "U", Entry = 1 });

        return cases;
    }

    #endregion

    #region Properties

    /// <summary>Every case.</summary>
    public static IReadOnlyList<SmtProjectionCase> All => ALL.Value;

    /// <summary>The names of the projections that are taken, for a test that takes one a case.</summary>
    public static IEnumerable<string> Taken => ALL.Value.Where(one => !one.IsRefused).Select(one => one.Id);

    /// <summary>The names of the projections that are refused, for a test that takes one a case.</summary>
    public static IEnumerable<string> Refused => ALL.Value.Where(one => one.IsRefused).Select(one => one.Id);

    #endregion
}
