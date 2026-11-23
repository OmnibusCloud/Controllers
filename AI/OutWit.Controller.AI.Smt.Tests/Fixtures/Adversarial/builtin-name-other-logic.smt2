; expect: accepted
; idea: under QF_UFNRA sin is no operator of the solver's, and a declared sin is the problem's function
; commands: 3
; canonical-has: (declare-fun sin (Real) Real)
; stripped: none
(set-logic QF_UFNRA)
(declare-fun sin (Real) Real)
(assert (= (sin 0.5) 11.0))
