; expect: syntax_error
; idea: a string whose last quote is doubled is not closed
; line: 5
(declare-const s String)
(assert (= s "abc""))
(check-sat)
