; expect: syntax_error
; idea: a backslash before the closing bar: one reader ends the symbol here, another reads on
; line: 4
(declare-const |a\| Bool)
(set-option :a 1)
(declare-const |b| Bool)
