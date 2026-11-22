; expect: reserved_prefix
; idea: a name with the prefix given to a term by :named
; line: 6
; command: assert
(declare-const p Bool)
(assert (! p :named oc!n))
