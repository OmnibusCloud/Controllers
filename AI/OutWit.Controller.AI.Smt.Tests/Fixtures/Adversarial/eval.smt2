; expect: command_not_allowed
; idea: eval is the solver's extension: evaluation is asked through the query
; line: 6
; command: eval
(declare-const x Int)
(eval x)
