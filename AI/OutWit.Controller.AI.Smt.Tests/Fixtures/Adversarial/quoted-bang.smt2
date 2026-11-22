; expect: reserved_symbol
; idea: the exclamation mark between bars around a named term
; line: 6
; command: assert
(declare-const p Bool)
(assert (|!| p :named n))
