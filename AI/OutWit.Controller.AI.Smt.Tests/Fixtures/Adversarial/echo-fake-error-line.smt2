; expect: command_not_allowed
; idea: echo prints a line that reads as the solver's error
; line: 5
; command: echo
(echo "(error ""line 1: out of memory"")")
(assert true)
