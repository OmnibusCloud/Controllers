; expect: command_not_allowed
; idea: include reads a file of the machine the solver runs on
; line: 6
; command: include
(set-logic QF_LIA)
(include "/etc/passwd")
(assert true)
