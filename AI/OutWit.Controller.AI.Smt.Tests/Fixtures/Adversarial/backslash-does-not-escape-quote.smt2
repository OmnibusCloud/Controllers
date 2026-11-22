; expect: command_not_allowed
; idea: a backslash does not escape a quote: the string ends, and the command after it is a command
; line: 7
; command: set-option
(declare-const s String)
(assert (= s "a\"))
(set-option :a 1)
(assert (= s "b"))
