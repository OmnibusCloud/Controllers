; expect: accepted
; idea: a constant named after an operator the solver has built in is the problem's, under ALL as well: the solver reads the declaration (six problems of the SMT-LIB slice declare pi)
; commands: 4
; canonical-has: (declare-fun pi () Real)
; canonical-has: (declare-const exp Real)
; stripped: none
(set-logic ALL)
(declare-fun pi () Real)
(declare-const exp Real)
(assert (> pi exp))
