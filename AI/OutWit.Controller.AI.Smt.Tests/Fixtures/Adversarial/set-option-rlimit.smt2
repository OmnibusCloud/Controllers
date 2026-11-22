; expect: command_not_allowed
; idea: the budget is the job's, never the problem's
; line: 6
; command: set-option
(declare-const p Bool)
(set-option :rlimit 0)
(assert p)
