; expect: accepted
; idea: questions to the solver may follow the check and the exit: they are stripped wherever they stand
; commands: 2
; stripped: check-sat=3
; stripped: exit=2
; stripped: get-info=2
; stripped: get-model=1
; stripped: get-value=1
(declare-const p Bool)
(get-info :version)
(assert p)
(check-sat)
(get-model)
(get-value (p))
(check-sat)
(exit)
(get-info :reason-unknown)
(check-sat)
(exit)
