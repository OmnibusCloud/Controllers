; expect: accepted
; idea: the same problem as crlf-line-endings, with line feeds
; commands: 4
; hash: c8a558a4c7000aa7eb10558869b7fae8e09877ea7635cf9186bd5bc4bb840996
(set-logic QF_LIA)
(set-info :source |a source
of two lines|)
(declare-const x Int)
(assert
  (> x 0))
