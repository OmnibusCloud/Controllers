; expect: accepted
; idea: a command inside a term is a term: the solver reports a function it does not know
; commands: 3
; canonical-has: (assert (push 1))
; canonical-has: (assert (exit))
; stripped: none
(assert (push 1))
(assert (set-option :produce-proofs true))
(assert (exit))
