; expect: command_after_end
; idea: a status given after the check it speaks of
; line: 8
; command: set-info
(declare-const p Bool)
(assert p)
(check-sat)
(set-info :status sat)
