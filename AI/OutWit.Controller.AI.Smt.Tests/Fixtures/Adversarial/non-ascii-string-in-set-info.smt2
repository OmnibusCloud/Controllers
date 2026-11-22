; expect: non_ascii_string
; idea: the rule of strings holds in a piece of information too
; line: 5
; command: set-info
(set-info :source "© 2025 the authors")
(assert true)
