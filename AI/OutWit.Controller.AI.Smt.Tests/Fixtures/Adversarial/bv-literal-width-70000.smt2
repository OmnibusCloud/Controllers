; expect: index_too_large
; idea: a bit-vector literal of 70,000 bits written with an index
; line: 6
; command: assert
(declare-const v (_ BitVec 8))
(assert (= v ((_ extract 7 0) (_ bv0 70000))))
