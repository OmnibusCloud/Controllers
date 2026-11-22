; expect: reserved_prefix
; idea: a name with the prefix bound by a quantifier
; line: 6
; command: assert
(assert (forall ((x Int)
                 (oc!z Int))
  (> (+ x oc!z) x)))
