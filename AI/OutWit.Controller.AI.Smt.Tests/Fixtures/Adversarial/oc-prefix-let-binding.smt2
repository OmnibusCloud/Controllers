; expect: reserved_prefix
; idea: a name with the prefix bound by let
; line: 6
; command: assert
(declare-const x Int)
(assert (let ((oc!y (+ x 1)))
  (> oc!y x)))
