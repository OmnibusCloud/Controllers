; expect: builtin_name
; idea: under ALL the solver takes a declared sin and still reads (sin x) as its own sine: the problem says sat, the solver unsat
; line: 6
; command: declare-fun
(set-logic ALL)
(declare-fun sin (Real) Real)
(assert (= (sin 0.5) 11.0))
