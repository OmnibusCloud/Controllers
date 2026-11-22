; expect: index_too_large
; idea: a rotation by a count past 32 bits
; line: 6
; command: assert
(declare-const v (_ BitVec 8))
(assert (= ((_ rotate_left 4294967296) v) v))
