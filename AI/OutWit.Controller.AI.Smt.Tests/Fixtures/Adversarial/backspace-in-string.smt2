; expect: syntax_error
; idea: a backspace inside a string
; line: 5
(declare-const s String)
(assert (= s "abc"))
