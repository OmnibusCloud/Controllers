; expect: syntax_error
; idea: a byte that is no UTF-8 inside a name
; line: 4
(declare-const aÿb Int)
