; expect: syntax_error
; idea: a command after a comment that ends with a carriage return only: a command to a reader that ends the comment there, comment to one that reads on to the line feed
; line: 5
(declare-const p Bool)
; a note(set-option :produce-proofs true)
(assert p)
