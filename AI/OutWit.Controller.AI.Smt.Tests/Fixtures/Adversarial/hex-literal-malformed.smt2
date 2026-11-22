; expect: syntax_error
; idea: a hexadecimal literal with letters that are no digits
; line: 5
(declare-const v (_ BitVec 8))
(assert (= v #xZZ))
