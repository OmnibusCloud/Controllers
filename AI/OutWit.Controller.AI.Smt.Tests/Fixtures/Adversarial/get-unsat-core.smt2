; expect: command_not_allowed
; idea: an unsat core
; line: 6
; command: get-unsat-core
(assert false)
(get-unsat-core)
