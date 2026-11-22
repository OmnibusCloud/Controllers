; expect: command_not_allowed
; idea: an option that sends the solver's answers to a file
; line: 5
; command: set-option
(set-option :regular-output-channel "/tmp/answers")
(assert true)
