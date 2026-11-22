; expect: syntax_error
; idea: a numeral with leading zeros is no numeral
; line: 5
(declare-const x Int)
(assert (> x 007))
