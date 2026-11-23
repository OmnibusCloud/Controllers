; expect: builtin_name
; idea: with no logic set the solver has its transcendental operators: a recursive definition of exp is not what (exp x) means to it
; line: 5
; command: define-fun-rec
(define-fun-rec exp ((x Real)) Real 1.0)
(assert (= (exp 0.5) 1.0))
