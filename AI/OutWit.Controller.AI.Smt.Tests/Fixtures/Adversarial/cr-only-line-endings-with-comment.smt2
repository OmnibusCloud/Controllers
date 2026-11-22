; expect: syntax_error
; idea: a file whose lines end with a carriage return only, with a comment: everything after the comment would be comment to one reader
; line: 4
(set-logic QF_LIA); the logic(declare-const x Int)(assert (> x 0))