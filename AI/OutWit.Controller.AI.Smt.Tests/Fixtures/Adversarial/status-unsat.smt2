; expect: accepted
; idea: the status is read as the expected verdict and never reaches the solver, which would report an error where it disagrees
; commands: 3
; canonical-has: (set-info :status unsat)
; solver-lacks: set-info
; solver-lacks: :status
; solver-lacks: unsat
; status: unsat
(set-info :status unsat)
(declare-const p Bool)
(assert p)
