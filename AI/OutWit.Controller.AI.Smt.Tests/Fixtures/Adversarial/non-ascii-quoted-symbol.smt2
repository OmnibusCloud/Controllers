; expect: accepted
; idea: a quoted symbol is a name, whatever it is made of
; commands: 2
; canonical-has: (declare-const |café| Int)
(declare-const |café| Int)
(assert (> |café| 0))
