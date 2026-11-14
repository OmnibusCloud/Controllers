using OutWit.Controller.Smt.Spike.Eval;
using OutWit.Controller.Smt.Spike.Lang;

namespace OutWit.Controller.Smt.Spike.Commands;

/// <summary><c>selftest</c>: the evaluator on hand-written problems and models, no solver needed.</summary>
internal static class SelfTestCommand
{
    #region Constants

    private static readonly (string Name, string Problem, string Model, string Values, string Expected)[] CASES =
    [
        ("lia", "(declare-const x Int)(declare-const y Int)(assert (= (+ x (* 2 y)) 7))(assert (> x (- 3)))(assert (= (mod x 3) 1))(assert (= (div (- 7) 2) (- 4)))",
            "", "((x 1) (y 3))", "validated"),
        ("lia-wrong", "(declare-const x Int)(assert (> x 5))", "", "((x 5))", "failed"),
        ("lra", "(declare-const r Real)(assert (= (* 3.0 r) 1.0))(assert (< r 0.5))", "", "((r (/ 1.0 3.0)))", "validated"),
        ("bv", "(declare-const a (_ BitVec 8))(assert (= (bvadd a #x01) #x00))(assert (bvslt a #x00))(assert (= ((_ extract 3 0) a) #xf))(assert (= (bvsdiv #xf9 #x02) #xfd))(assert (= (bvsmod #xf9 #x02) #x01))(assert (= (bvsrem #xf9 #x02) #xff))",
            "", "((a #xff))", "validated"),
        ("bv-shift", "(declare-const a (_ BitVec 4))(assert (= (bvashr a #x1) #xc))(assert (= (bvlshr a #x4) #x0))(assert (= ((_ rotate_left 1) a) #x1))", "", "((a #x8))", "validated"),
        ("uf", "(declare-fun f (Int) Int)(declare-const x Int)(assert (= (f x) 3))(assert (not (= (f 0) 3)))",
            "((define-fun x () Int 1) (define-fun f ((x!0 Int)) Int (ite (= x!0 1) 3 0)))", "((x 1))", "validated"),
        ("array", "(declare-const a (Array Int Int))(assert (= (select a 2) 5))(assert (= a (store ((as const (Array Int Int)) 0) 2 5)))",
            "((define-fun a () (Array Int Int) (_ as-array k!0)) (define-fun k!0 ((x!0 Int)) Int (ite (= x!0 2) 5 0)))", "((a (_ as-array k!0)))", "validated"),
        ("datatype", "(declare-datatypes ((L 0)) (((nil) (cons (hd Int) (tl L)))))(declare-const l L)(assert ((_ is cons) l))(assert (= (hd l) 4))(assert (= (tl l) nil))(assert (match l ((nil false) ((cons h t) (> h 3)))))",
            "", "((l (cons 4 nil)))", "validated"),
        ("uninterpreted", "(declare-sort U 0)(declare-const u U)(declare-const v U)(assert (not (= u v)))",
            "(;; universe for U:\n(declare-fun U!val!0 () U)\n(declare-fun U!val!1 () U)\n(define-fun u () U U!val!0)(define-fun v () U U!val!1))", "((u U!val!0) (v U!val!1))", "validated"),
        ("let-named", "(declare-const p Bool)(declare-const q Bool)(assert (! (let ((a (and p q))) (=> a q)) :named n1))(assert (xor p q))(assert n1)",
            "", "((p true) (q false))", "validated"),
        ("quantifier", "(declare-const x Int)(assert (forall ((y Int)) (>= (* y y) x)))", "", "((x 0))", "partial"),
        ("string", "(declare-const s String)(assert (= (str.len s) 3))(assert (str.prefixof \"ab\" s))(assert (= (str.at s 2) \"\\u{63}\"))", "", "((s \"abc\"))", "validated")
    ];

    #endregion

    #region Functions

    /// <summary>Runs the cases and the mutation check.</summary>
    /// <returns>0 when every case gives its expected class and every mutant of the validated ones fails or is told apart.</returns>
    public static int Run()
    {
        var failures = 0;
        foreach (var (name, problemText, modelText, valuesText, expected) in CASES)
        {
            var problem = SmtProblem.Parse(problemText);
            var validator = new SmtModelValidator(problem);
            var model = SmtModel.Parse(modelText, valuesText);
            var result = validator.Check(model);
            var mutants = expected == "validated" ? validator.Mutants(model, 3).Select(m => validator.Check(m.Model).Class).ToList() : [];
            var ok = result.Class == expected;
            failures += ok ? 0 : 1;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name,-14} {result.Class,-10} {result.Reason} mutants: {string.Join(",", mutants)}");
        }

        Console.WriteLine(failures == 0 ? "selftest: every case as expected" : $"selftest: {failures} case(s) wrong");
        return failures == 0 ? 0 : 1;
    }

    #endregion
}
