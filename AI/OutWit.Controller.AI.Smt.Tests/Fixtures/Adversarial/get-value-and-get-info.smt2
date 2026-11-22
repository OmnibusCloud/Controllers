; expect: accepted
; idea: questions to the solver are stripped whatever they ask
; commands: 1
; stripped: get-info=2
; stripped: get-value=1
(declare-const x Int)
(get-value (x (+ x 1)))
(get-info :reason-unknown)
(get-info :all-statistics)
