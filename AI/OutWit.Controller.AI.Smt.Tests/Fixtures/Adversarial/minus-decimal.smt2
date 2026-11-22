; expect: ambiguous_token
; idea: a negative decimal written as one token
; line: 6
; command: assert
(declare-const r Real)
(assert (> r -0.5))
