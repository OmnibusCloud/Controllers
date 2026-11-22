; expect: command_malformed
; idea: two datatypes named, one declared
; line: 5
; command: declare-datatypes
(declare-datatypes ((A 0) (B 0)) (((a))))
