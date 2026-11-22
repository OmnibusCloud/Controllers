; expect: accepted
; idea: the last status holds
; commands: 3
; status: unsat
(set-info :status sat)
(assert true)
(set-info :status unsat)
