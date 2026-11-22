; expect: command_after_end
; idea: an assertion after exit: a solver given the file never sees it
; line: 8
; command: assert
(declare-const p Bool)
(assert p)
(exit)
(assert (not p))
