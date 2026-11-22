; expect: accepted
; idea: a carriage return and a line feed end a comment as a line feed does
; commands: 2
; canonical-has: (assert p)
; canonical-lacks: exit
; canonical-lacks: note
(declare-const p Bool) ; a note
; a whole line (exit)
(assert p)
