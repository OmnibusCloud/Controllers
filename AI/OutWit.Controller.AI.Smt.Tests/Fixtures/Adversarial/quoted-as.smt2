; expect: reserved_symbol
; idea: as between bars around a qualified name
; line: 6
; command: assert
(declare-datatype L ((nil) (cons (hd Int) (tl L))))
(assert (= (|as| nil L) nil))
