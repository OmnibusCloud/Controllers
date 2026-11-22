; expect: accepted
; idea: a command name is an ordinary symbol everywhere but at the head of a command: a solver takes it as a name, and tools that print for it write it bare
; commands: 2
; canonical-has: (declare-const exit Bool)
; canonical-has: (assert exit)
; canonical-lacks: |
; stripped: none
(declare-const exit Bool)
(assert exit)
