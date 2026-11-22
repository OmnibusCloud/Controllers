; expect: reserved_symbol
; idea: par between bars in a datatype
; line: 5
; command: declare-datatype
(declare-datatype P (|par| (T) ((mk (get T)))))
