; expect: builtin_name
; idea: the second function of define-funs-rec is named tanh, between bars, which the solver drops
; line: 6
; command: define-funs-rec
(set-logic ALL)
(define-funs-rec ((twice ((n Int)) Int) (|tanh| ((x Real)) Real)) ((* 2 n) x))
