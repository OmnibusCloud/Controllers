# OutWit.Controller.AI.Smt.Model

The language layer and the data types of the OutWit Smt controller: SMT problems solved by a
pinned solver on anonymous nodes, with every answer checked.

This page describes **0.1.0**: the reader, the sanitizer with the canonical form of a problem and
the script builders, and the model evaluator that checks a `sat` answer. The controller is in
development; the wire types of a run join this package before a controller that needs them is
published.

## Reading SMT-LIB

Everything that looks at a problem before a solver does reads it with the same code: the
application that prepares a run, the host that splits it, the node that solves it.

| Type | Role |
|---|---|
| `SmtReader` | reads SMT-LIB 2.6 text into s-expressions: a script, a term, a model a solver printed |
| `SmtExpression` | one s-expression: a list, or an atom with its text; written back, it is one line of single-spaced tokens that reads as the same s-expression |
| `SmtExpressionKind` | list, symbol, keyword, numeral, decimal, hexadecimal, binary, string |
| `SmtReaderLimits` | how much the reader takes before it refuses: characters, tokens, nesting, digits of a number, bits of a literal, characters of a string; `SmtReaderLimits.Problem` holds the ceilings of a submitted problem |
| `SmtSyntaxException` / `SmtSyntaxFault` | a refusal: what is wrong, the line and the column, a sentence without the text itself |
| `SmtReservedWords` | the reserved words of the standard: the general ones and the command names |
| `SmtLexicon` | the character classes the reader and the writer share: blanks, delimiters, the characters of a simple symbol |

```csharp
using OutWit.Controller.AI.Smt.Model.Lang;

var commands = SmtReader.Read(File.ReadAllText("problem.smt2"), SmtReaderLimits.Problem);
foreach (var command in commands)
    Console.WriteLine($"line {command.Line}: {command.Head}");
```

The reader takes the standard's lexicon and nothing beside it, and refuses the places where
readers are known to differ, so that a text means the same to it as to a conforming solver:

- blanks are the space, the tab, the line feed and the carriage return; a form feed, a no-break
  space or a byte order mark is refused;
- a bare token is a simple symbol, a keyword, a numeral, a decimal, or a hexadecimal or binary
  literal in full: `12abc`, `007`, `#x`, `a'b` and `1.5e3` are refused;
- a quoted symbol holds no backslash;
- a comment ends at a line feed, and a carriage return in a comment that no line feed follows
  is refused: the standard ends the comment there, a solver that ends it at the line feed only
  reads on, and the two would read different texts;
- a string literal or a quoted symbol holds no control character but the tab and the line
  breaks, and no half of a surrogate pair.

`-5` is a simple symbol, as the standard has it; a consumer that hands text to a solver which
reads `-5` as a number refuses such a symbol by a rule of its own.

A comment is dropped. A symbol between bars is the simple symbol of the same name (`|abc|` is
`abc`) and is written without bars, except a reserved word, which stays between them. Nesting is
kept on a stack of the reader's own: a term two hundred thousand deep is read, written and
compared without a deep call stack, and how deep a submitted problem may nest is a limit, not
a property of the thread. The limit is far from small: a term that is used many times is
written as a chain of bindings, one inside the other, and real problems nest more than ten
thousand levels deep.

| Ceiling of a submitted problem | |
|---|---|
| Characters | 4,194,304 |
| Tokens (atoms and parentheses) | 2,000,000 |
| Nesting depth | 100,000 |
| Digits of a numeral or a decimal | 4,096 |
| Bits of a hexadecimal or binary literal | 65,536 |
| Characters of a string literal | 65,536 |

## Sanitizing a problem

A problem states something: it declares, defines and asserts. The sanitizer takes the text of
one problem and gives either the problem or a refusal, before any solver sees the text. The same
code looks at a problem in the application, on the host and on the node, so a problem is the
same problem, with the same hash, everywhere.

| Type | Role |
|---|---|
| `SmtSanitizer` | `Sanitize(text)` for a problem, `SanitizeTerm(text)` for a term that stands beside one (a goal to prove, a term whose value is asked, a constant to project on) |
| `SmtProblem` | the problem as taken: its commands, its two texts and its hash, the logic, the expected verdict, the attribution, the declared constants and functions, what was stripped |
| `SmtRefusalData` | a refusal: the line, the command, a fixed code, a reason. It never carries the text: at most one token of it, cut to 64 characters |
| `SmtRefusalCodes` | the codes of a refusal |
| `SmtScripts` | the commands the controller itself puts around a problem |

```csharp
using OutWit.Controller.AI.Smt.Model.Lang;

var result = SmtSanitizer.Sanitize(File.ReadAllText("problem.smt2"));
if (result.IsAccepted)
    Console.WriteLine($"{result.Problem.Hash}: {result.Problem.AssertionCount} assertions, expects {result.Problem.Status}");
else
    Console.WriteLine($"line {result.Refusal!.Line}: {result.Refusal.Code}: {result.Refusal.Reason}");
```

### What becomes of a command

| | Commands | |
|---|---|---|
| Kept | `set-logic`, `set-info`, `declare-sort`, `define-sort`, `declare-const`, `declare-fun`, `define-fun`, `define-fun-rec`, `define-funs-rec`, `declare-datatype`, `declare-datatypes`, `assert` | in the problem, in the order of the text |
| Stripped | `check-sat`, `get-model`, `get-value`, `get-info`, `exit` | counted and left out: the query decides what runs. A kept command after `check-sat` or after `exit` is refused: the problem ends there |
| Refused | `push`, `pop`, `reset`, `reset-assertions`, `check-sat-assuming`, `get-unsat-core`, `get-unsat-assumptions`, `get-assertions`, `get-assignment`, `get-proof`, `get-option` | incremental scripts, unsat cores and proofs are not supported |
| Refused | `set-option` | the controller sets every option itself |
| Refused | `echo`, and every other name: the solver's extensions (`include`, `eval`, `simplify`, `define-const`, ...) and names nobody has heard of | a command is never passed on because nothing was known against it |

A kept command is held to the shape the standard gives it: how many parts it has, and that a
name stands where a name must stand: a symbol that is no general reserved word (`let`, `par`,
`_` and the rest). A sort or a term is any s-expression here: what they mean is the solver's to
check.

A command name is an ordinary symbol everywhere but at the head of a command:
`(declare-const exit Bool)` declares a constant, as it does to a solver, and tools that print
for a solver write such names bare. Bare or between bars, a command name is a name and a
symbol of a term, and it is written back as it was read.

### Refusal codes

| Code | When |
|---|---|
| `syntax_error` | the text is not SMT-LIB: a character the lexicon has no place for, an unbalanced parenthesis, a literal left open, a token that is none of the lexicon's |
| `text_too_long`, `too_many_tokens`, `nesting_too_deep`, `numeral_too_long`, `literal_too_wide`, `string_too_long` | the text is past a ceiling of a submitted problem. `text_too_long` also when the problem's own canonical text would be past the ceiling of characters |
| `not_a_command` | a top-level form that is an atom, an empty list, or a list that does not start with a symbol |
| `command_not_allowed` | a command that is neither kept nor stripped; a command name between bars (`(\|assert\| ...)`) |
| `command_after_end` | a kept command after `check-sat` (a script that goes on after a check is incremental: its first answer is for less than the whole text) or after `exit` (a solver runs nothing after it). Stripped commands may follow either |
| `command_malformed` | a kept command in another shape than the standard's |
| `reserved_symbol` | a general reserved word between bars (`\|let\|`, `\|_\|`), anywhere in a command the solver is given or in a term: an ordinary symbol by the standard, the reserved word to the solver |
| `ambiguous_token` | a symbol that starts with a minus sign and a digit (`-5`), anywhere in a command the solver is given or in a term: a symbol by the standard, a negative number to the solver |
| `non_ascii_string` | in a command the solver is given or in a term, a string literal with a character outside printable ASCII, the tab and the line breaks: the theory of strings takes such a character as the escape `\u{...}` |
| `index_too_large` | in a command the solver is given or in a term, an index above 65,536 in a list that starts with `_`: a bit-vector's width, an extension, a repetition, a rotation, and every other index |
| `too_many_symbols` | more than 100,000 declared names: constants, functions and sorts (declared or defined), datatypes, their constructors and their selectors |
| `reserved_prefix` | a symbol that starts with `oc!`, the prefix of the controller's own names, anywhere in a command the solver is given or in a term: declared, bound by a binder, given by `:named`, or merely used |
| `not_a_term` | a text given as one term holds no s-expression, or more than one |

Where good SMT-LIB would mean something else to the solver than to the standard, the problem is
refused, not reinterpreted. The first thing in the text that breaks a rule is the one named; a
text that is not SMT-LIB is refused as such whatever commands it starts with. A stripped or
refused command is not looked into.

The rules of single tokens are about what the solver reads. A `set-info` never reaches it, so
it is held to its shape only and kept as it was read: a `:source` with an author's name
outside ASCII is taken, and the canonical text and the hash carry it as UTF-8.

### The canonical form and its hash

`SmtProblem.Canonical` is every kept command, one a line, as `SmtExpression.WriteTo` writes it,
every line ended by a line feed. `SmtProblem.Hash` is the SHA-256 of its UTF-8 bytes, in
lower-case hexadecimal: the name of the problem.

- The canonical text is a fixed point: sanitized again, it gives the same text and the same hash.
- The hash depends on the kept commands' tokens and on nothing else: not on comments, on blanks
  and line endings between tokens, on bars around a name that needs none, or on stripped commands.
- Nor does it depend on how a file ends the lines of a text inside a `set-info` (the source of a
  problem is usually several lines): there a line break is written as one line feed, so the file
  with line feeds and the file with carriage returns are the same problem.
- Any other change of a kept token changes it. Outside `set-info`, a line break inside a string
  literal or a quoted symbol is part of that token.

### Why no `set-info` reaches the solver

`SmtProblem.SolverText` is the canonical text without any `set-info`. The solver acts on some
pieces of information: a `:status` that disagrees with its verdict makes it report an error
instead of answering, and `:smt-lib-version` changes how it reads a string. The controller reads
what it needs itself (`:status` as the expected verdict when it is `sat` or `unsat`, `:source`
and `:license` for an attribution list), the person who submitted the problem still sees every
`set-info` line in the canonical text, and the solver is given none.

### The controller's own commands

`SmtScripts` builds the commands that go around a problem (`check-sat`, `get-model`, `get-value`,
`get-info`, `set-option`, the resource limit, the negated goal of a proof, the assertion that
rules a found model out) as s-expressions, written by the writer that writes a problem. A term
goes into a command as the s-expression it is, never as text: whatever it holds, the command is
one command. A resource limit above 4,294,967,295 is refused: the solver's parameter is 32 bits
wide, and given more it reports an error and then runs with no limit at all.

## Checking a model

A `sat` answer comes with a model, and a model can be checked: every assertion of the problem is
evaluated under it. The evaluator is plain .NET code that shares nothing with a solver, so an
answer it validates needs no trust in whoever gave it.

| Type | Role |
|---|---|
| `SmtModelValidator` | built once from a problem (the `SmtProblem` the sanitizer took, or a list of its commands); checks any number of models, from any number of threads |
| `SmtModel` | what the solver printed after `sat`: the answer of `(get-model)`, of `(get-value (...))`, or both |
| `SmtValidation` | the outcome: class, reason, detail, and the counts behind them |
| `SmtValidationClass` | `Validated`, `Partial`, `Failed` |
| `SmtValidationReasons` | the reason codes |
| `SmtEvaluationBudget` | what one validation may spend: steps, nesting, sizes |
| `SmtClosedScript` | the problem with the model written into it, for a solver to evaluate |

```csharp
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Model.Lang;

var problem = SmtSanitizer.Sanitize(problemText).Problem!;  // the problem as the sanitizer took it
var validator = new SmtModelValidator(problem);
var model = SmtModel.Read(modelText, valuesText, limits);   // never throws: a text that is no model is an unreadable model
var validation = validator.Validate(model, SmtEvaluationBudget.Default);

Console.WriteLine($"{validation.Class} {validation.Reason} {validation.Detail}");
```

### The three classes

| Class | Means |
|---|---|
| `Validated` | every assertion evaluated to true. The model, with a default value for each symbol it leaves out, is a witness: the problem is satisfiable |
| `Failed` | an assertion evaluated to false with nothing guessed. The model does not satisfy the problem: the answer is wrong |
| `Partial` | neither. The answer rests on other checks |

`Failed` is an accusation, so it is given only when it is certain: no symbol the model left out
stands behind the false assertion, no construct the evaluator does not compute to the end, no
budget. `Validated` is given only for a real witness: every value the model gives a symbol is
checked against the sort the problem declares for it, wherever it is used. A model that sets an
`Int` to a half satisfies nothing.

A validation never throws for what the problem or the model holds, never runs past its budget,
and gives the same answer for the same input on every machine.

### What is evaluated

- Core: `not`, `and`, `or`, `=>`, `xor`, `=`, `distinct`, `ite`; `let`, `!` with `:named` terms
  used by name, `match`.
- Ints and Reals, exactly: integers of any size and rationals in lowest terms; `div` and `mod`
  by the standard's definition, where the remainder is never negative (`(div (- 7) 2)` is
  `(- 4)`, `(mod (- 7) 2)` is `1`, `(div 7 (- 2))` is `(- 3)`).
- Bit-vectors: every operator of QF_BV, the signed divisions by the standard's own definitions
  (division by zero included, which the standard defines for bit-vectors), `bv2nat`, `int2bv`,
  and the three spellings of a value (`#x..`, `#b..`, `(_ bvN w)`).
- Arrays, as finite maps over a default: `((as const (Array I E)) v)`, `store`, `select`,
  equality. A function the solver prints behind an array (`(_ as-array f)`, `lambda`) becomes a
  finite map where its body is the chain of `ite` a solver prints, and can be read at an index
  otherwise.
- Datatypes: constructors, selectors, `(_ is C)` and `is-C`, `match`; the declarations of
  SMT-LIB 2.6 and 2.5, with `par`.
- Uninterpreted sorts, with the elements the model lists, and uninterpreted functions, with the
  model's interpretations.
- `define-fun`, `define-fun-rec`, `define-funs-rec`.
- The basic operators of strings: `str.++`, `str.len`, `str.at`, `str.substr`, `str.prefixof`,
  `str.suffixof`, `str.contains`, `str.indexof`, `str.replace`, `str.<`, `str.<=`, the
  conversions to and from integers and code points.

`and`, `or` and `=>` are evaluated in three values: a false conjunct decides a conjunction
whatever a conjunct beside it would have come to. `ite` evaluates only the branch its condition
picks. A recursive definition is taken as the program it reads as.

A general reserved word (`let`, `forall`, `exists`, `match`, `par`, `_`, `!`, `as`) is the word
only when it is not written between bars. The name of a command (`assert`, `reset`, `push` and
the others) is an ordinary name in a term, with bars or without, and the model's names are
matched to the problem's by the name alone.

### What is partial

| Reason | When |
|---|---|
| `completed_symbol` | an assertion is false only with a symbol the model left out, which was given the default value of its sort |
| `quantifier` | `forall`, `exists` |
| `algebraic_number` | a number the solver printed as `root-obj` |
| `division_by_zero` | `div`, `mod` or `/` by zero: the standard leaves the value to the model |
| `regular_expression` | `str.in_re` and everything under `re.` |
| `floating_point` | the theory of floating-point numbers |
| `selector_of_other_constructor` | a selector applied to a value of another constructor |
| `unsupported_operator` | an operator the evaluator knows by name and does not compute; the detail is the name |
| `unsupported_sort` | a sort it has no values for; an array as the index of an array |
| `array_comparison` | two arrays whose equality their finite maps do not decide |
| `string_literal` | a string literal readers read differently: an escape that is none of the standard's, a character outside printable ASCII |
| `ill_formed_term` | a term that fits no reading: a wrong number of arguments, a value of the wrong sort |
| `unknown_symbol` | a name that nothing declares |
| `ill_formed_command` | a command that is no declaration, definition or assertion, or one that cannot be read |
| `ambiguous_symbol` | a name the problem gives two meanings: declared twice, or the name of an operator |
| `unreadable_model` | a text that is not what a solver prints as a model |
| `budget_steps`, `budget_depth`, `budget_size` | the budget ended the evaluation |
| `evaluator_error` | a defect of the evaluator itself; the tests hold it to zero occurrences |

`Failed` has one reason, `assertion_false`, with the 1-based index of the first false assertion
as its detail. A reason is a fixed code: nothing of the problem or of the model is ever in it.

### The budget

A validation counts what it spends and stops, as `Partial`, when the count passes the budget.
Nothing is timed.

| Budget | Default | Counts |
|---|---|---|
| Steps | 20,000,000 | one for each term evaluated; an operation on a large value pays for the machine words it touches, a value for the memory it takes |
| Nesting | 32,768 | a term in a term, a call (two levels), a value in a value; a budget may ask for up to 131,072 |
| Bits of an integer | 1,048,576 | also the numerator and the denominator of a rational together |
| Bits of a bit-vector | 1,048,576 | |
| Characters of a string | 1,048,576 | |
| Stored indices of an array | 1,048,576 | |

Terms are walked by calls, on a thread the validation makes for itself with a stack sized for
the nesting ceiling: an eighth of a gigabyte is reserved for the default ceiling, and only what
a term really nests is ever taken of it. The chains real terms are written as cost no nesting
at all, and are not bounded by the ceiling: a chain of
`let`, each in the body of the one before; of `ite`, each in a branch of the one before; of
`and`, `or` or `=>`, each the last argument of the one before; annotated terms; the cases of a
`match`. A problem written as a quarter of a million nested `let` is evaluated like any other.

### The closed script

`SmtClosedScript.Build(commands, model)` writes the model into the problem: every declared
symbol the model defines becomes a `define-fun` with the sorts the problem declares, after the
elements of the model's universes and the functions the solver added for itself, each
definition after the ones it mentions; the problem's own definitions and assertions follow, and
`(check-sat)` ends it. `set-info` is left out. A solver that is handed the script has nothing
left to choose: it evaluates, and its `sat` or `unsat` is a second opinion on the evaluator's
`Validated` or `Failed`. A symbol the model leaves out stays declared, and `Free` counts them.
