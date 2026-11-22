; expect: accepted
; idea: a comment is dropped whatever it holds
; commands: 1
; canonical-lacks: set-option
; canonical-lacks: push
; canonical-lacks: exit
; stripped: none
; (set-option :a 1) (exit)
(assert true) ; (push 1)
