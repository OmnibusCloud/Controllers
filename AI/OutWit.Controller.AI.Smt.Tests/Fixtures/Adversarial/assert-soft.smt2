; expect: command_not_allowed
; idea: a soft assertion is the solver's extension
; line: 6
; command: assert-soft
(declare-const p Bool)
(assert-soft p :weight 2)
