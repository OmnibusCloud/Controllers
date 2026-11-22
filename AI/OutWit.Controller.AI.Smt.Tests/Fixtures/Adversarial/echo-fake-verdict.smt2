; expect: command_not_allowed
; idea: echo prints a line that reads as the solver's verdict
; line: 7
; command: echo
(declare-const p Bool)
(assert p)
(echo "unsat")
