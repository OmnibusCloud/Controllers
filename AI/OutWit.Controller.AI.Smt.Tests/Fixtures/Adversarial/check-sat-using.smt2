; expect: command_not_allowed
; idea: a tactic chosen by the problem
; line: 7
; command: check-sat-using
(declare-const p Bool)
(assert p)
(check-sat-using (then simplify smt))
