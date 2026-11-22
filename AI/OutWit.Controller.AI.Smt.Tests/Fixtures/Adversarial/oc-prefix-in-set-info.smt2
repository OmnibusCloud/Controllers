; expect: accepted
; idea: a piece of information is none of the solver's names
; commands: 2
; canonical-has: (set-info :source oc!generator)
; solver-lacks: oc!
(set-info :source oc!generator)
(assert true)
