; expect: accepted
; idea: four quotes are the string of one quote
; commands: 2
; canonical-has: (assert (= s """"))
(declare-const s String)
(assert (= s """"))
