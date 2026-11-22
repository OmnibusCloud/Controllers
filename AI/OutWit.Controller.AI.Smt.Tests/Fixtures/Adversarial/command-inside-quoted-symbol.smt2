; expect: accepted
; idea: a quoted symbol that holds commands stays one name
; commands: 2
; canonical-has: (assert |(check-sat) (exit)|)
; stripped: none
(declare-const |(check-sat) (exit)| Bool)
(assert |(check-sat) (exit)|)
