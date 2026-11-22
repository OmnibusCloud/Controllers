; expect: command_malformed
; idea: two functions declared, one body
; line: 5
; command: define-funs-rec
(define-funs-rec ((f ((x Int)) Int) (g ((x Int)) Int)) ((g x)))
