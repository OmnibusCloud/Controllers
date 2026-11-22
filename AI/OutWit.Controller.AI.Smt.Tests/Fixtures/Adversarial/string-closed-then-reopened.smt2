; expect: accepted
; idea: a second string that swallows the next commands is a string
; commands: 3
; stripped: none
(declare-const s String)
(declare-const t String)
(assert (= s "abc" "))
(set-option :a 1)
(assert (= t "))
