; expect: accepted
; idea: what asks the solver to act is stripped and counted; the commands after it are kept
; commands: 3
; canonical-has: (assert (not p))
; canonical-lacks: check-sat
; canonical-lacks: exit
; canonical-lacks: get-model
; stripped: check-sat=1
; stripped: exit=1
; stripped: get-model=1
(declare-const p Bool)
(check-sat)
(assert p)
(exit)
(assert (not p))
(get-model)
