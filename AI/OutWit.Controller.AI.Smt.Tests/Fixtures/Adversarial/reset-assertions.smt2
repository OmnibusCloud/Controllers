; expect: command_not_allowed
; idea: reset-assertions would take back what was asserted before it
; line: 6
; command: reset-assertions
(assert false)
(reset-assertions)
