; expect: command_malformed
; idea: constructors without their parentheses: the solver's shorthand, not the standard's
; line: 5
; command: declare-datatypes
(declare-datatypes ((Color 0)) ((red green blue)))
