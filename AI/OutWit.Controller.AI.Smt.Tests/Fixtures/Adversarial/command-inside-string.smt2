; expect: accepted
; idea: a string that holds commands stays one string
; commands: 2
; canonical-has: "(exit) (set-option :a 1) (check-sat)"
; stripped: none
(declare-const s String)
(assert (= s "(exit) (set-option :a 1) (check-sat)"))
