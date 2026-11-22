; expect: command_not_allowed
; idea: an incremental script
; line: 6
; command: push
(declare-const x Int)
(push 1)
(assert (> x 0))
(pop 1)
(assert (< x 0))
