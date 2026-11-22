; expect: accepted
; idea: a quoted symbol that runs over lines of commands is one name
; commands: 1
; canonical-has: (exit)
; stripped: none
(declare-const |x Bool)
(exit)
(assert |x)
