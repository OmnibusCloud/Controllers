; expect: accepted
; idea: a piece of information never reaches the solver and is kept as it was read: a source outside ASCII is taken
; commands: 2
; canonical-has: (set-info :source "© 2025 Søren and the authors")
; solver-lacks: 2025
; solver-lacks: set-info
(set-info :source "© 2025 Søren and the authors")
(assert true)
