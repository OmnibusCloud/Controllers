; expect: accepted
; idea: two quotes inside a string are one quote of it, not its end
; commands: 3
; canonical-has: "a"")) (exit) (assert (= t ""b"
; stripped: none
(declare-const s String)
(declare-const t String)
(assert (= s "a"")) (exit) (assert (= t ""b"))
