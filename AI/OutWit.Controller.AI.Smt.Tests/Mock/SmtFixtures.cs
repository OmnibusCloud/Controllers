namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// Hand-written problems of the logics the evaluator covers, each with a model that satisfies
/// it, spelled as a solver prints models: negative numbers as <c>(- 3)</c>, rationals as
/// <c>(/ 1.0 3.0)</c>, bit-vectors as <c>#x..</c>, <c>#b..</c> or <c>(_ bvN w)</c>.
/// </summary>
internal static class SmtFixtures
{
    #region Constants

    private static readonly SmtFixture[] NUMBERS =
    [
        new("lia-linear", "QF_LIA", """
            (set-logic QF_LIA)
            (declare-const x Int)
            (declare-const y Int)
            (declare-const z Int)
            (assert (= (+ x (* 2 y)) 7))
            (assert (> x (- 3)))
            (assert (= (mod x 3) 1))
            (assert (= (div (- 7) 2) (- 4)))
            (assert (<= (- z) (abs y)))
            (assert (distinct x y z))
            (assert (< z x y 10))
            (assert (= (- z x) (- 3)))
            """,
            "(\n  (define-fun x () Int\n    1)\n  (define-fun y () Int\n    3)\n  (define-fun z () Int\n    (- 2))\n)",
            "((x 1)\n (y 3)\n (z (- 2)))"),

        new("lia-let-ite", "QF_LIA", """
            (set-logic QF_LIA)
            (declare-fun a () Int)
            (declare-fun b () Int)
            (declare-fun p () Bool)
            (assert (let ((s (+ a b)) (d (- a b))) (and (= s 10) (= d 4))))
            (assert (=> p (> a 100)))
            (assert (ite p (< b 0) (= b 3)))
            (assert (or p (>= (* 3 a) 21) false))
            (assert ((_ divisible 7) (+ a 14)))
            """,
            null,
            "((a 7) (b 3) (p false))"),

        new("lia-definitions", "QF_LIA", """
            (set-logic QF_LIA)
            (define-fun double ((n Int)) Int (* 2 n))
            (define-fun-rec fact ((n Int)) Int (ite (<= n 1) 1 (* n (fact (- n 1)))))
            (define-funs-rec ((even ((n Int)) Bool) (odd ((n Int)) Bool))
              ((ite (= n 0) true (odd (- n 1))) (ite (= n 0) false (even (- n 1)))))
            (declare-const k Int)
            (declare-const p Bool)
            (declare-const q Bool)
            (assert (= (double k) 10))
            (assert (= (fact k) 120))
            (assert (odd k))
            (assert (! (let ((a (and p q))) (=> a q)) :named n1))
            (assert (xor p q))
            (assert n1)
            """,
            "((define-fun k () Int 5) (define-fun p () Bool true) (define-fun q () Bool false))",
            null),

        new("lra-rationals", "QF_LRA", """
            (set-logic QF_LRA)
            (declare-const r Real)
            (declare-const s Real)
            (declare-const t Real)
            (assert (= (* 3.0 r) 1.0))
            (assert (< r 0.5))
            (assert (= (+ r s) (/ 5 6)))
            (assert (>= (* 2 s) 1))
            (assert (is_int (* 6 r)))
            (assert (= (+ t r) 0.0))
            (assert (= (to_int (* 10.0 s)) 5))
            (assert (< t 0 r s 1.5))
            """,
            "((define-fun r () Real (/ 1.0 3.0)) (define-fun s () Real (/ 1.0 2.0)) (define-fun t () Real (- (/ 1.0 3.0))))",
            "((r (/ 1.0 3.0)) (s (/ 1.0 2.0)) (t (- (/ 1.0 3.0))))"),

        new("lra-decimals", "QF_LRA", """
            (set-logic QF_LRA)
            (declare-const u Real)
            (declare-const v Real)
            (assert (= (- u v) 2.75))
            (assert (= (* 4 v) (- 1)))
            (assert (not (is_int u)))
            (assert (= (to_real 5) (* 2.0 u)))
            """,
            null,
            "((u 2.5) (v (- 0.25)))")
    ];

    private static readonly SmtFixture[] BIT_VECTORS =
    [
        new("bv-arithmetic", "QF_BV", """
            (set-logic QF_BV)
            (declare-const a (_ BitVec 8))
            (declare-const b (_ BitVec 8))
            (declare-const w (_ BitVec 16))
            (assert (= (bvadd a #x01) #x00))
            (assert (bvslt a #x00))
            (assert (= ((_ extract 3 0) a) #xf))
            (assert (= (bvsdiv #xf9 #x02) #xfd))
            (assert (= (bvsmod #xf9 #x02) #x01))
            (assert (= (bvsrem #xf9 #x02) #xff))
            (assert (= (concat a b) w))
            (assert (= (bvlshr w #x0004) #x0ff1))
            (assert (bvult b a))
            (assert (= ((_ zero_extend 8) b) (bvand w #x00ff)))
            (assert (= ((_ sign_extend 8) a) #xffff))
            (assert (= ((_ rotate_left 4) b) #x21))
            (assert (= ((_ repeat 2) b) #x1212))
            (assert (= (bv2nat b) 18))
            (assert (= ((_ int2bv 8) 274) b))
            (assert (= (bvmul b #x03) (bvsub #x40 #x0a)))
            (assert (= (bvudiv w #x0000) #xffff))
            (assert (= (bvurem b #x00) b))
            """,
            "((define-fun a () (_ BitVec 8) #xff) (define-fun b () (_ BitVec 8) #x12) (define-fun w () (_ BitVec 16) #xff12))",
            "((a #xff) (b #x12) (w #xff12))"),

        new("bv-shifts", "QF_BV", """
            (set-logic QF_BV)
            (declare-const a (_ BitVec 4))
            (declare-const c (_ BitVec 4))
            (declare-const one (_ BitVec 1))
            (assert (= (bvashr a #x1) #xc))
            (assert (= (bvlshr a #x4) #x0))
            (assert (= ((_ rotate_left 1) a) #x1))
            (assert (= ((_ rotate_right 1) a) #b0100))
            (assert (= (bvshl c #x2) #xc))
            (assert (= (bvcomp a c) #b0))
            (assert (= (bvnot c) (bvxor c #xf)))
            (assert (= (bvor (bvnand a c) (bvnor a c)) (bvxnor a (bvneg a))))
            (assert (and (bvuge a c) (bvsle a c) (bvsgt c a) (bvule c a) (bvugt a c) (bvsge c a)))
            (assert (= one ((_ extract 3 3) a)))
            """,
            null,
            "((a (_ bv8 4)) (c #b0111) (one #b1))")
    ];

    private static readonly SmtFixture[] STRINGS =
    [
        new("strings", "QF_S", """
            (set-logic QF_S)
            (declare-const s String)
            (declare-const t String)
            (assert (= (str.len s) 3))
            (assert (str.prefixof "ab" s))
            (assert (str.suffixof "bc" s))
            (assert (= (str.at s 2) "\u{63}"))
            (assert (= t (str.++ s "!" s)))
            (assert (str.contains t "c!a"))
            (assert (= (str.indexof t "b" 2) 5))
            (assert (= (str.replace s "b" "") "ac"))
            (assert (= (str.substr t 2 3) "c!a"))
            (assert (str.< s t))
            (assert (str.<= s s))
            (assert (= (str.to_int "42") 42))
            (assert (= (str.from_int 7) "7"))
            (assert (= (str.to_code "a") 97))
            (assert (= (str.from_code 98) "b"))
            (assert (str.is_digit "5"))
            """,
            "((define-fun s () String \"abc\") (define-fun t () String \"abc!abc\"))",
            "((s \"abc\") (t \"abc!abc\"))")
    ];

    #endregion

    #region Properties

    /// <summary>Every fixture: each is validated as it stands.</summary>
    public static IReadOnlyList<SmtFixture> All { get; } = [.. NUMBERS, .. BIT_VECTORS, .. STRINGS, .. SmtStructuredFixtures.All];

    #endregion
}
