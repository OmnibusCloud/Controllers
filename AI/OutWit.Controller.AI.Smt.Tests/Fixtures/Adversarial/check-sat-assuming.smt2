; expect: command_not_allowed
; idea: a check under assumptions
; line: 6
; command: check-sat-assuming
(declare-const p Bool)
(check-sat-assuming (p))
