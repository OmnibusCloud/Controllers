; expect: index_too_large
; idea: an extraction above the widest bit-vector
; line: 6
; command: assert
(declare-const v (_ BitVec 8))
(assert (= v ((_ extract 99999 99992) v)))
