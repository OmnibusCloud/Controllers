; expect: index_too_large
; idea: a bit-vector repeated 100,000 times
; line: 6
; command: assert
(declare-const v (_ BitVec 8))
(assert (= ((_ repeat 100000) v) ((_ repeat 100000) v)))
