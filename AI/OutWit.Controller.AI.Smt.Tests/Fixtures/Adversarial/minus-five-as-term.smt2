; expect: ambiguous_token
; idea: -5 is a symbol by the standard and a number to the solver
; line: 6
; command: assert
(declare-const x Int)
(assert (> x -5))
