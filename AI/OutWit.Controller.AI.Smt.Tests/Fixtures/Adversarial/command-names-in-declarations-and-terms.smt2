; expect: accepted
; idea: reset, push, pop, echo and assert as the names of a sort, of functions, of a constructor and of bound variables
; commands: 5
; canonical-has: (define-fun get-model ((exit reset)) Int (push exit))
; stripped: none
(declare-sort reset 0)
(declare-fun push (reset) Int)
(declare-datatype echo ((pop (assert Int))))
(define-fun get-model ((exit reset)) Int (push exit))
(assert (forall ((check-sat reset)) (let ((set-option (push check-sat))) (> set-option 0))))
