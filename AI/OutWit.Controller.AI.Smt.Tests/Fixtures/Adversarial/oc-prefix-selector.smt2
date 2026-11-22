; expect: reserved_prefix
; idea: a selector with the prefix
; line: 6
; command: declare-datatypes
(declare-datatypes ((D 0)) (
  ((mk (oc!get Int)))))
