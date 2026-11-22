; expect: accepted
; idea: a command name between bars is a name, and keeps its bars
; commands: 2
; canonical-has: (declare-const |exit| Bool)
; canonical-has: (assert |exit|)
; stripped: none
(declare-const |exit| Bool)
(assert |exit|)
