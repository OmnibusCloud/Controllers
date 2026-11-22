; expect: command_not_allowed
; idea: optimization is the solver's extension
; line: 7
; command: maximize
(declare-const x Int)
(assert (< x 10))
(maximize x)
