; expect: command_not_allowed
; idea: a stripped command's name between bars
; line: 6
; command: check-sat
(assert true)
(|check-sat|)
