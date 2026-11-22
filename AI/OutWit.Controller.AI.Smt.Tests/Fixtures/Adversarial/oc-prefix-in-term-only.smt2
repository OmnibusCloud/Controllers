; expect: reserved_prefix
; idea: a name with the prefix that is used and never declared: it could be one the controller declares after the problem
; line: 6
; command: assert
(declare-const x Int)
(assert (> x oc!limit))
