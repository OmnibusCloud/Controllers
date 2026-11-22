; expect: command_after_end
; idea: an assertion after a check: an incremental script, whose first answer is for less than the whole text
; line: 7
; command: assert
(declare-const p Bool)
(check-sat)
(assert p)
(check-sat)
