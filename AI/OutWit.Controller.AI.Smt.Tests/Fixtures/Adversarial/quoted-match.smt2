; expect: reserved_symbol
; idea: match between bars
; line: 6
; command: assert
(declare-const x Int)
(assert (|match| x ((y true))))
