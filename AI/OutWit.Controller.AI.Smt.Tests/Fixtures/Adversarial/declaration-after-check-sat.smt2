; expect: command_after_end
; idea: a declaration after a check
; line: 9
; command: declare-const
(declare-const p Bool)
(assert p)
(check-sat)
(get-model)
(declare-const q Bool)
