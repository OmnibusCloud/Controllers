; expect: accepted
; idea: a version that would change how the solver reads strings never reaches it
; commands: 3
; canonical-has: (set-info :smt-lib-version 2.0)
; solver-lacks: set-info
; solver-lacks: smt-lib-version
; solver-lacks: 2.0
(set-info :smt-lib-version 2.0)
(declare-const s String)
(assert (= s "a"))
