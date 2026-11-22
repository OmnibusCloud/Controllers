; expect: ambiguous_token
; idea: -5 between bars loses its bars when written, and is the number again
; line: 5
; command: declare-const
(declare-const |-5| Int)
(assert (> |-5| 0))
