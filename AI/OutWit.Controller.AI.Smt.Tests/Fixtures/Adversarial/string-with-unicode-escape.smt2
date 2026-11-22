; expect: accepted
; idea: a character outside ASCII written as the theory of strings writes it
; commands: 2
; canonical-has: "caf\u{e9}"
(declare-const s String)
(assert (= s "caf\u{e9}"))
