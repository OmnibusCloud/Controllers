; expect: command_not_allowed
; idea: a command nobody has heard of is refused, never passed on
; line: 6
; command: frobnicate
(assert true)
(frobnicate 1 2 3)
