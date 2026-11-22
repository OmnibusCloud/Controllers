; expect: command_not_allowed
; idea: reset would take back what was asserted before it
; line: 6
; command: reset
(assert false)
(reset)
(assert true)
