; expect: ambiguous_token
; idea: -5 declared as a constant
; line: 5
; command: declare-const
(declare-const -5 Int)
(assert (> -5 0))
