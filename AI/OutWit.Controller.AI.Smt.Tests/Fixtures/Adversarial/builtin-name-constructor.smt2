; expect: builtin_name
; idea: a constructor with a field named log, in the second datatype: the solver reads (log x) as its logarithm
; line: 6
; command: declare-datatypes
(set-logic ALL)
(declare-datatypes ((Shape 0) (Op 0)) (((circle (radius Real))) ((log (arg Real)) (id))))
(assert (= (log 2.0) (log 2.0)))
