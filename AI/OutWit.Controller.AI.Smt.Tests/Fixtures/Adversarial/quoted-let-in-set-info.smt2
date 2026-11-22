; expect: accepted
; idea: the rules of single tokens are about what the solver reads: a reserved word between bars and a minus sign before a digit are taken in a piece of information
; commands: 2
; canonical-has: (set-info :note (|let| -5 (_ a 99999)))
; solver-lacks: let
; solver-lacks: 99999
(set-info :note (|let| -5 (_ a 99999)))
(assert true)
