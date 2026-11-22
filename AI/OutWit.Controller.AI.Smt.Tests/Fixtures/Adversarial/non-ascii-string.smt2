; expect: non_ascii_string
; idea: a string holds printable ASCII: any other character is written as an escape
; line: 6
; command: assert
(declare-const s String)
(assert (= s "café"))
