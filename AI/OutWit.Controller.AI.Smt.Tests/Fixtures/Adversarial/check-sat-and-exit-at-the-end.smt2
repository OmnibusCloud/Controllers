; expect: accepted
; idea: what asks the solver to act is stripped and counted
; commands: 2
; canonical-has: (assert p)
; canonical-lacks: check-sat
; canonical-lacks: exit
; canonical-lacks: get-model
; stripped: check-sat=1
; stripped: exit=1
; stripped: get-model=1
(declare-const p Bool)
(assert p)
(check-sat)
(get-model)
(exit)
