; expect: index_too_large
; idea: a bit-vector extended by 99,999 bits
; line: 6
; command: assert
(declare-const v (_ BitVec 8))
(assert (= ((_ zero_extend 99999) v) ((_ zero_extend 99999) v)))
