; expect: accepted
; idea: bars around a name that needs none are not part of the problem
; commands: 2
; canonical-has: (declare-const x Int)
; canonical-lacks: |
(declare-const |x| Int)
(assert (> |x| 0))
