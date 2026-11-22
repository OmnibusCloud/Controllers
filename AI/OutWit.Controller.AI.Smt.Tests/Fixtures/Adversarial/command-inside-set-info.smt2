; expect: accepted
; idea: a command as the value of a piece of information never reaches the solver
; commands: 2
; canonical-has: (set-info :note (exit))
; solver-lacks: exit
; solver-lacks: set-info
; stripped: none
(set-info :note (exit))
(assert true)
