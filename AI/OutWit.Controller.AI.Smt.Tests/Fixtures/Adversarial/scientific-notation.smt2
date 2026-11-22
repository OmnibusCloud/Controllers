; expect: syntax_error
; idea: the standard has no exponent
; line: 5
(declare-const r Real)
(assert (> r 1.5e3))
