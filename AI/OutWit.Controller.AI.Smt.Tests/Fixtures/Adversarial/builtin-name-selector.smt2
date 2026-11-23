; expect: accepted
; idea: a selector named cos takes a datatype, which the solver's cosine does not, and a constructor without fields named sin is a constant: both are the problem's
; commands: 3
; canonical-has: (declare-datatype Wave ((sin) (mk (cos Real))))
; stripped: none
(set-logic ALL)
(declare-datatype Wave ((sin) (mk (cos Real))))
(assert (= (cos (mk 1.0)) 1.0))
