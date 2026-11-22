; expect: accepted
; idea: a comment ends at a line feed only: what follows a lone carriage return is still comment, and is dropped
; commands: 2
; canonical-has: (assert p)
; canonical-lacks: set-option
(declare-const p Bool)
; a note(set-option :produce-proofs true)
(assert p)
