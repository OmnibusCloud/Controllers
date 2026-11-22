; expect: accepted
; idea: a name that is used and never declared is the solver's to report
; commands: 2
; canonical-has: oc!limit
(declare-const x Int)
(assert (> x oc!limit))
