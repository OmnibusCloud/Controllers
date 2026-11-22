namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// The fixtures of the logics with structure: uninterpreted sorts and functions, arrays and
/// datatypes. The models are spelled as a solver prints them: the elements of a universe as
/// declarations with the cardinality constraint after them, a function as a chain of
/// <c>ite</c>, an array as stores over a constant array or as a function of the solver's own.
/// </summary>
internal static class SmtStructuredFixtures
{
    #region Constants

    private const string UNIVERSE_MODEL = """
        (
          ;; universe for U:
          ;;   U!val!0 U!val!1
          ;; -----------
          ;; definitions for universe elements:
          (declare-fun U!val!0 () U)
          (declare-fun U!val!1 () U)
          ;; cardinality constraint:
          (forall ((x U)) (or (= x U!val!0) (= x U!val!1)))
          ;; -----------
          (define-fun u () U
            U!val!0)
          (define-fun v () U
            U!val!1)
          (define-fun f ((x!0 U)) U
            (ite (= x!0 U!val!1) U!val!0
              U!val!1))
          (define-fun g ((x!0 U) (x!1 U)) Bool
            (and (= x!0 U!val!0) (= x!1 U!val!1)))
        )
        """;

    private const string ARRAY_MODEL = """
        (
          (declare-fun Index!val!0 () Index)
          (declare-fun Index!val!1 () Index)
          (forall ((x Index)) (or (= x Index!val!0) (= x Index!val!1)))
          (declare-fun Element!val!0 () Element)
          (declare-fun Element!val!1 () Element)
          (forall ((x Element)) (or (= x Element!val!0) (= x Element!val!1)))
          (define-fun a () (Array Index Element)
            ((as const (Array Index Element)) Element!val!0))
          (define-fun b () (Array Index Element)
            (store ((as const (Array Index Element)) Element!val!0) Index!val!0 Element!val!1))
          (define-fun i () Index Index!val!0)
          (define-fun j () Index Index!val!1)
          (define-fun e () Element Element!val!1)
        )
        """;

    private static readonly SmtFixture[] FIXTURES =
    [
        new("uf-universe", "QF_UF", """
            (set-logic QF_UF)
            (declare-sort U 0)
            (declare-fun f (U) U)
            (declare-fun g (U U) Bool)
            (declare-const u U)
            (declare-const v U)
            (assert (not (= u v)))
            (assert (= (f u) v))
            (assert (= (f (f u)) u))
            (assert (g u v))
            (assert (not (g v u)))
            (assert (distinct u (f u)))
            """,
            UNIVERSE_MODEL,
            "((u U!val!0) (v U!val!1))"),

        new("uf-propositional", "QF_UF", """
            (set-logic QF_UF)
            (declare-fun p () Bool)
            (declare-fun q () Bool)
            (declare-fun r () Bool)
            (assert (or p q))
            (assert (=> p q r))
            (assert (not (and p r)))
            (assert (= q (not p)))
            (assert (xor p q r))
            """,
            "((define-fun p () Bool false) (define-fun q () Bool true) (define-fun r () Bool false))",
            "((p false) (q true) (r false))"),

        new("ax-stores", "QF_AX", """
            (set-logic QF_AX)
            (declare-sort Index 0)
            (declare-sort Element 0)
            (declare-const a (Array Index Element))
            (declare-const b (Array Index Element))
            (declare-const i Index)
            (declare-const j Index)
            (declare-const e Element)
            (assert (= b (store a i e)))
            (assert (not (= i j)))
            (assert (= (select b j) (select a j)))
            (assert (not (= (select a i) e)))
            (assert (not (= a b)))
            (assert (= a (store b i (select a i))))
            """,
            ARRAY_MODEL,
            null),

        new("alia-as-array", "QF_ALIA", """
            (set-logic QF_ALIA)
            (declare-const a (Array Int Int))
            (declare-const m (Array Int (Array Int Int)))
            (declare-const n Int)
            (assert (= (select a 2) 5))
            (assert (= a (store ((as const (Array Int Int)) 0) 2 5)))
            (assert (= (select (select m 1) n) (+ n 4)))
            (assert (> n 2))
            (assert (= (select m 0) ((as const (Array Int Int)) 0)))
            """,
            """
            (
              (define-fun a () (Array Int Int)
                (_ as-array k!0))
              (define-fun m () (Array Int (Array Int Int))
                (store ((as const (Array Int (Array Int Int))) ((as const (Array Int Int)) 0)) 1 (store ((as const (Array Int Int)) 0) 3 7)))
              (define-fun n () Int
                3)
              (define-fun k!0 ((x!0 Int)) Int
                (ite (= x!0 2) 5
                  0))
            )
            """,
            "((n 3))"),

        new("alia-lambda", "QF_ALIA", """
            (set-logic QF_ALIA)
            (declare-const a (Array Int Int))
            (declare-const flags (Array Int Bool))
            (assert (= (select a 2) 5))
            (assert (= (select a 3) 0))
            (assert (select flags 4))
            (assert (not (select flags 5)))
            (assert (= (store a 3 1) (store (store ((as const (Array Int Int)) 0) 3 1) 2 5)))
            """,
            null,
            "((a (lambda ((x!1 Int)) (ite (= x!1 2) 5 0))) (flags (lambda ((x!1 Int)) (or (= x!1 4) (= x!1 6)))))"),

        new("uflia-functions", "QF_UFLIA", """
            (set-logic QF_UFLIA)
            (declare-fun f (Int) Int)
            (declare-fun h (Int Int) Int)
            (declare-const x Int)
            (assert (= (f x) 3))
            (assert (not (= (f 0) 3)))
            (assert (= (h x (f x)) (+ x 10)))
            (assert (< (h 0 0) 0))
            """,
            """
            (
              (define-fun x () Int
                1)
              (define-fun f ((x!0 Int)) Int
                (ite (= x!0 1) 3
                  0))
              (define-fun h ((x!0 Int) (x!1 Int)) Int
                (ite (and (= x!0 1) (= x!1 3)) 11
                  (- 1)))
            )
            """,
            "((x 1))"),

        new("dt-list", "QF_DT", """
            (set-logic QF_DT)
            (declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))
            (declare-const l L)
            (assert ((_ is cons) l))
            (assert (= (hd l) 4))
            (assert (= (tl l) nil))
            (assert (match l ((nil false) ((cons h t) (> h 3)))))
            (assert (is-cons l))
            (assert (not (= l (cons 4 (cons 4 nil)))))
            """,
            "((define-fun l () L (cons 4 nil)))",
            "((l (cons 4 nil)))"),

        new("dt-mutual-parametric", "QF_DT", """
            (set-logic QF_DT)
            (declare-datatypes ((Tree 0) (Forest 0))
              (((leaf) (node (children Forest)))
               ((none) (more (first Tree) (rest Forest)))))
            (declare-datatypes ((Pair 2)) ((par (A B) ((mk (fst A) (snd B))))))
            (declare-datatype Color ((red) (green) (blue)))
            (declare-datatypes () ((Suit hearts spades)))
            (declare-const t Tree)
            (declare-const p (Pair Int Bool))
            (declare-const c Color)
            (declare-const s Suit)
            (assert (= t (node (more leaf none))))
            (assert (and (= (fst p) 2) (snd p)))
            (assert (match t ((leaf false) ((node f) ((_ is more) f)))))
            (assert (distinct c red green))
            (assert (match c ((red false) (other (= other blue)))))
            (assert (= s spades))
            """,
            null,
            "((t (node (more leaf none))) (p (mk 2 true)) (c blue) (s spades))"),

        new("ufdt-rank", "QF_UFDT", """
            (set-logic QF_UFDT)
            (declare-datatypes ((Nat 0)) (((zero) (succ (pred Nat)))))
            (declare-fun rank (Nat) Int)
            (declare-const k Nat)
            (assert (= (rank zero) 0))
            (assert (= (rank k) 2))
            (assert (= k (succ (succ zero))))
            (assert (= (pred k) (succ zero)))
            """,
            """
            (
              (define-fun k () Nat
                (succ (succ zero)))
              (define-fun rank ((x!0 Nat)) Int
                (ite (= x!0 (succ (succ zero))) 2
                  0))
            )
            """,
            "((k (succ (succ zero))))")
    ];

    #endregion

    #region Properties

    /// <summary>The fixtures with structure.</summary>
    public static IReadOnlyList<SmtFixture> All => FIXTURES;

    #endregion
}
