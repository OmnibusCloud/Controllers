# OutWit.Controller.AI.Smt.Model

The language layer and the data types of the OutWit Smt controller: SMT problems solved by a
pinned solver on anonymous nodes, with every answer checked.

This page describes **0.3.0**: the reader, the sanitizer with the canonical form of a problem and
the script builders, the problem set (its rows, its reader, its schema, the options of a run),
the model evaluator that checks a `sat` answer, the wire types of a node's work (a package of
problems, a row, a package of rows, the diagnostics), and the types of a run on the host (its
preflight and the words of its documents). The controller is in development; nothing is
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
| `builtin_name` | in a problem whose logic is `ALL`, or that has set none before the command, a function declared or defined with parameters, or a constructor with fields, named `acos`, `acosh`, `arctan2`, `asin`, `asinh`, `atan`, `atan2`, `atanh`, `cos`, `cosh`, `exp`, `ln`, `log`, `sin`, `sinh`, `tan` or `tanh`: there the solver takes the declaration and still reads every application of the name as its own operator |
| `not_a_term` | a text given as one term holds no s-expression, or more than one |

The codes of a row of a problem set (`row_malformed`, `field_invalid` and the rest) are in
"A problem set" below.

Where good SMT-LIB would mean something else to the solver than to the standard, the problem is
refused, not reinterpreted. The first thing in the text that breaks a rule is the one named; a
text that is not SMT-LIB is refused as such whatever commands it starts with. A stripped or
refused command is not looked into.

A problem may give its own meaning to a name the solver has built in. For every such name but
the ones of `builtin_name`, the solver then either reports an error or reads an application
that fits the problem's declaration as the problem's, and a constant of any such name (`pi`,
`exp`) is the problem's: those problems are taken. Under any other logic than `ALL` the names
of `builtin_name` are no operators of the solver's, and a function of such a name is taken too.

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

## A problem set

What a lab submits is a problem set: JSON lines, one problem a line, UTF-8, with the options of
the run beside it. The same code reads a set in the application before anybody signs in, on the
host when a run is split, and wherever a preflight counts what a set holds.

| Type | Role |
|---|---|
| `SmtProblemSetReader` | reads a set from a stream a row at a time: `TryRead(out row)`; holds one line in memory, never the set, and keeps a running `Summary` |
| `SmtProblemSetRow` | one row: its `Line` and `Index`, and either the problem (`Data`, and the sanitizer's `Problem` beside it) or the `Refusal` |
| `SmtProblemData` | an accepted problem as it travels to a node: its id, its query, its canonical text and hash, the terms beside it as canonical text, the expected verdict, the tags |
| `SmtProblemSetSummary` | what was read, counted: rows taken and refused, the taken problems by logic, by query and by expected verdict, the stripped commands, the refusals by code and the first 1,000 of them in full, the licences |
| `SmtProblemSetLimits` | the numbers below |
| `SmtOptionsData`, `SmtOptionsRules` | the options of a run; their defaults and ceilings, `Check` and `Resolve` |
| `SmtQuery` | `Check`, `Enumerate`, `Prove` |

```csharp
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;

var options = new SmtOptionsData { Query = SmtQuery.Check };
foreach (var problem in SmtOptionsRules.Check(options))
    Console.WriteLine(problem);

using var stream = File.OpenRead("problems.jsonl");
var reader = new SmtProblemSetReader(stream, options);
while (reader.TryRead(out var row))
{
    if (row.IsAccepted)
        Console.WriteLine($"{row.Data.Id}: {row.Data.Query}, {row.Data.Hash}");
    else
        Console.WriteLine($"line {row.Line}: {row.Refusal!.Code}: {row.Refusal.Reason}");
}

Console.WriteLine($"{reader.Summary.Accepted} taken, {reader.Summary.Refused} refused");
```

### A row

```json
{"id": "cage-6x6-000412", "query": "enumerate", "smt2": "(declare-const a Int)(declare-const b Int)(assert (< 0 a b 4))", "project": ["a", "b"], "max_models": 2}
{"id": "vc-17", "query": "prove", "smt2": "(declare-const x Int)(assert (> x 1))", "goal": "(> (* x x) x)"}
```

The schema of a row is [`docs/smt-problems.schema.json`](docs/smt-problems.schema.json), packed
with the package. A row is taken exactly when the schema allows it, wherever a schema can state
the rule; a row it would not take is refused, by the field's name.

| Field | Rule |
|---|---|
| `id` | required; 1 to 64 characters, each a letter `A-Z` or `a-z`, a digit, or one of `. _ - / + : @ =`; once in a set (the first row with an id keeps it, whatever becomes of the row) |
| `smt2` | required; the text of the problem, sanitized as above |
| `query` | optional: `check`, `enumerate` or `prove`; without it the query of the run's options |
| `values` | `check` only; optional; 1 to 256 strings, each one term whose value is asked |
| `project` | `enumerate` only, and required there; 1 to 256 strings, each the name of a constant the problem declares (by `declare-const`, or by a `declare-fun` of no arguments), none named twice |
| `max_models` | `enumerate` only; optional; an integer from 1 to 1,000; without it the number of the run's options, and without that 2 |
| `goal` | `prove` only, and required there; one term |
| `expected` | `check` and `enumerate` only; optional; `sat` or `unsat`; it holds over the problem's `:status`. A `prove` row expects nothing |
| `tags` | optional; at most 256 characters without control characters, passed through. A character is one Unicode code point, as the schema's `maxLength` counts it |

A term (of `values`, of `goal`, a name of `project`) is exactly one s-expression of at most
65,536 characters, held to the rules of single tokens; it travels as its canonical text, and a
name of `project` as the problem declares it.

Around the row: a line is at most 16 MiB (16,777,216 bytes), its line end not counted, and a
longer one is refused and passed over without being held in memory; a line that is empty or only
blanks is no row; a line ends with a line feed, with or without a carriage return before it; a
byte order mark at the very start is skipped. A set has at most 1,000,000 rows: the row after
that is refused once, and reading ends. A refused row does not stop the reading.

### The codes of a row

A refusal of a row is an `SmtRefusalData` with `Row`, the line of the set; `File`, the row's id
(empty when the row has no readable id); and `Line`, the line inside the problem's or the term's
text for a refusal of the sanitizer, 0 for a refusal of the row itself. A refusal of the
sanitizer keeps its own code from the table above; for a term the reason starts with the field
(`'goal': ...`, `'values[2]': ...`). A reason names a field or a single token cut to 64
characters, never more of the row.

| Code | When |
|---|---|
| `row_malformed` | the line is not valid UTF-8 or not one JSON object (nothing of such a line is read, its id included), or the row gives a field twice |
| `row_too_long` | the line is longer than 16 MiB; nothing of it is read |
| `id_invalid` | the id is not 1 to 64 of the characters an id is made of |
| `id_duplicate` | an earlier row of the set has the id |
| `field_missing` | the row lacks `id` or `smt2`, an `enumerate` row lacks `project`, a `prove` row lacks `goal` |
| `field_invalid` | a field's value is of another type (null included), outside its range, none of the words the field takes, or not valid Unicode text; an array is empty or too long; a name of `project` is no symbol, or names a constant twice |
| `field_not_allowed` | the row holds a name that is no field of a row (a misspelt field is one), or a field of another query than the row's |
| `unknown_constant` | a name of `project` is not a constant the problem declares |
| `too_many_problems` | the set has more than 1,000,000 rows; this row and what follows it were not read |

What is looked at, in this order: the line is a row and every field that was given reads (the
first fault in the order of the line); the row has its id, and no earlier row has it; it has its
problem; every field belongs to the row's query and the query has the fields it needs; the
sanitizer takes the problem; it takes the terms, and the names are declared constants.

### The options of a run

| Option (`SmtOptionsData`) | In a run's specification | Default | Ceiling | |
|---|---|---|---|---|
| `Profile` | `profile` | the controller's | | the id of the solver profile; this package only carries it |
| `Query` | `query` | `check` | | the query of a row that names none |
| `MaxModels` | `max_models` | 2 | 1,000 | the most models of an enumeration whose row names no number |
| `Seed` | `seed` | 0 | 2,147,483,647 | the seed of the solver's random choices |
| `ResourceLimit` | `rlimit` | the profile's | 4,294,967,295 | the solver's own counter of work for one check |
| `HardCapSeconds` | `hard_cap_s` | 60 | 600 | seconds of a reference machine for one problem; the controller turns them into a budget of the solver's work that is the same on every node |
| `MemoryMegabytes` | `memory_mb` | 1,024 | 4,096 | the most memory of one problem |
| `SamplingRate` | `sampling_rate` | 0.05 | 1 | the share of the problems that are run on two machines; 0 turns re-execution off, 1 runs every problem twice |

A number that is 0 and a text that is empty mean "not set: the default". The sampling rate is the
exception, since 0 is a rate of its own: a new `SmtOptionsData` holds its default.
`SmtOptionsRules.Check` gives one sentence for each option that is out of its range, naming it as
a run's specification does; `SmtOptionsRules.Resolve` gives a copy with the defaults of this
package filled in (the profile and the resource limit stay as they are: their defaults are the
controller's and the profile's). A reader takes options that are in range, and refuses to be
made with others.

## Checking a model

A `sat` answer comes with a model, and a model can be checked: every assertion of the problem is
evaluated under it. An answer may also carry the values of terms (the answer of `get-value`), and
those are a claim about the model that is checked too. The evaluator is plain .NET code that
shares nothing with a solver, so an answer it validates needs no trust in whoever gave it.

| Type | Role |
|---|---|
| `SmtModelValidator` | built once from a problem (the `SmtProblem` the sanitizer took, or a list of its commands); checks any number of models, each with the values that came with it, from any number of threads |
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

// Never throws: a text that is no model is an unreadable model. The limits say how much of a
// text the reader takes: SmtReaderLimits.Unbounded for a text whose size is bounded already (a
// row of a results document), limits of your own for a text from anywhere else.
var model = SmtModel.Read(modelText, valuesText, SmtReaderLimits.Unbounded);

// The terms the row asked the values of (its "values"; for an enumeration its "project"), in
// the row's order; an empty list when it asked for none.
var asked = askedTexts.Select(text => SmtSanitizer.SanitizeTerm(text).Term!).ToList();
var validation = validator.Validate(model, asked, SmtEvaluationBudget.Default);

Console.WriteLine($"{validation.Class} {validation.Reason} {validation.Detail}");
```

The one call checks both: the model against the problem, and the values against the terms asked
and against the model. `Validate(model, budget)`, without the terms, is for a caller that does
not know what was asked: each pair of the values is then held to the term it names itself. In
neither call is a values text read and left unchecked.

### The three classes

| Class | Means |
|---|---|
| `Validated` | every assertion evaluated to true, and every reported value is the value its term has under the model. The model, with a default value for each symbol it leaves out, is a witness: the problem is satisfiable |
| `Failed` | an assertion evaluated to false, or a reported value is another than its term's, with nothing guessed. The model does not satisfy the problem, or does not give the values that came with it: the answer is wrong |
| `Partial` | neither. The answer rests on other checks |

`Failed` is an accusation, so it is given only when it is certain: no symbol the model left out
stands behind the false assertion or the other value, no construct the evaluator does not
compute to the end, no budget. `Validated` is given only for a real witness: every value the
model gives a symbol is checked against the sort the problem declares for it, wherever it is
used. A model that sets an `Int` to a half satisfies nothing.

A validation never throws for what the problem or the model holds, never runs past its budget,
and gives the same answer for the same input on every machine.

The evaluator is open code, so its agreement with the solver is shown by volume, not by
reading: its tests hold it to the pinned solver on thousands of closed terms generated for every
theory below, and on the models of real problems written back into their problems (the closed
script). Wherever the evaluator gives a value, the solver agrees with it.

### The values of terms

The answer of `get-value` is a list of pairs, a term and its value. It is a claim about the
model, never a second model: the assertions are evaluated under the definitions of `get-model`,
and no value stands in for a definition. Then every pair is checked.

- The answer must be the answer of what was asked: as many pairs as terms, each pair's term
  written as the term asked at its place. A solver echoes a term as it was written to it (the
  pinned Z3 does, character for character), so an honest answer always reads so. An answer that
  does not (unreadable, another number of pairs, another term, values where none were asked or
  none where some were) is used for nothing, and the validation is `Partial` at best, with
  `unreadable_values`.
- The term is evaluated under the model, the value is read, and the two are compared as values,
  not as texts: `#x0f`, `#b00001111` and `(_ bv15 8)` are one value, as are `0.5` and
  `(/ 1.0 2.0)`, and two orders of the stores of an array.
- Another value, with nothing guessed, is `Failed` with `value_mismatch`; the detail is the
  1-based index of the value among those reported.
- A term or a value the evaluator does not compute (the reasons below), a comparison it does not
  decide (`array_comparison`), a term that rests on a symbol the model leaves out
  (`completed_symbol`): `Partial`. A value is never validated unread, and an answer is never
  failed for what the evaluator cannot decide.
- A value is written with nothing of the problem. One that reads a symbol the problem declares
  or defines, itself or through a name the model wrote with one ("the value of `x` is `x`"), may
  be true and says nothing: `Partial`, with `unreadable_values`. A value may name what the model
  itself gives a value: an element of a sort, the function behind an array.

One kind of pair adds to the model. A solver that is asked for the value of a constant its model
leaves open completes the model there, so the first value of a constant the problem declares and
the answer of `get-model` does not define is that constant's value, and the assertions are
evaluated with it. This is also what makes an answer of `get-value` alone a model by itself:
`SmtModel.Read(null, valuesText, limits)`. The pinned Z3 prints every symbol a problem declares
in its model, so its own answers hold no such pair.

The values are checked after the assertions, from the same budget.

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
- Uninterpreted sorts, with the elements the model lists and the ones it names without listing
  them (below), and uninterpreted functions, with the model's interpretations. Two elements of
  a sort are the same element exactly when they have the same name.
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
| `unreadable_values` | a text that is not the answer of `get-value` for the terms asked: no list of pairs, another number of pairs, another term, or a value written with a symbol of the problem |
| `budget_steps`, `budget_depth`, `budget_size` | the budget ended the evaluation |
| `evaluator_error` | a defect of the evaluator itself; the tests hold it to zero occurrences |

`Failed` has two reasons: `assertion_false`, with the 1-based index of the first false assertion
as its detail, and `value_mismatch`, with the 1-based index of the first reported value that is
another than its term's. A false assertion is reported before a value. A reason is a fixed code:
nothing of the problem or of the model is ever in it.

### The budget

A validation counts what it spends, for the assertions and the reported values together, and
stops, as `Partial`, when the count passes the budget. Nothing is timed.

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

A solver names an element of an uninterpreted sort after the sort (`A!val!0` of a sort `A`),
and may give a function such an element as its value without listing that sort's universe.
Such an element, named in the model's definitions, of a sort the problem declares without
parameters, and declared by nobody, is declared with the listed ones, and `Unlisted` counts
them: the script names nothing it does not declare. The evaluator reads such a name by the same
rule, as an element of its sort like a listed one; a name of that shape for a sort the problem
does not declare, or declares with parameters, stays an unknown symbol.

## A node's work

Problems travel to a node and rows travel back as blobs: what the messages carry are references.
These are the types of both ends; the package format of the blobs is the controller's own.

| Type | Role |
|---|---|
| `SmtTaskPackageData` | what a node is handed: the blob of the problems (`ProblemsBlobId`, `Sha256`, `SizeBytes`), how many (`ProblemCount`), the package's `Ticket`, the controller version the job runs on (`RequiredControllerVersion`), the digest of the problems' indices (`IndexDigest`), the run's `Options`, resolved |
| `SmtResultPackageData` | what a node returns: the blob of the rows (`ResultsBlobId`, `Sha256`, `SizeBytes`), how many (`ResultCount`), the ticket it answers, and a `FaultCode` with its `FaultMessage` when the node could not work on the package at all |
| `SmtResultData` | one problem's row: the problem (`Index`, `Id`, the `Hash` of the text the node was given), the `Ticket`, how its session ended and what it came to, every model and value as the solver printed it, what it cost, the `ControllerVersion`, the `Platform`, the `WallMs`, and its `Digest` |
| `SmtResultDigest` | the canonical form of a row and its SHA-256: `Of(row)`, `Holds(row)`, `Canonical(row)` |
| `SmtResultWords` | the words of a row: statuses, verdicts, reasons, proofs |
| `SmtNodeFaults` | the codes of a package a node could not work on |
| `SmtRuntimeDiagnosticsData` | what the controller of a process carries and can do |

The ticket is 16 random bytes as 32 lower-case hex characters, made with the package; it comes
back in the result package and in every row, and is never written to a document. The index
digest is the SHA-256 over the indices of the package's problems in ascending order, each as four
bytes, low byte first.

### The row of a problem

| Field | |
|---|---|
| `Status` | `ok`, `refused`, `input_error`, `output_exceeded`, `engine_crash`, `node_fault` |
| `Code`, `Message` | the fixed word of the outcome's kind and its sentence (at most 1,024 characters): a refusal's code and reason, the solver's message of an input error, the kind of a crash or of a fault |
| `Verdict` | `sat`, `unsat`, `unknown`, `none` |
| `Reason`, `ReasonText` | for an `unknown`: `rlimit`, `fuel`, `memout`, `incomplete`, `other` (else `none`), and the solver's own word |
| `Proof` | `proved`, `counterexample`, `none` |
| `Count`, `Complete`, `LimitReached` | what an enumeration found |
| `Models`, `Values` | as the solver printed them, all of them: what a lab is shown of them is the report's to cut |
| `Checks`, `ResourceCount`, `Fuel`, `PeakMemoryBytes` | what the session cost, in counts |
| `Transcript` | the last 8 KB of the session's transcript |

A row names no machine: it says the platform (`win-x64`, `linux-x64`, `osx-arm64`) and the
controller version, and nothing else of the node. A row whose status is `node_fault` says
something about the machine and nothing about the problem: the problem has no answer from it.

### The digest of a row

Everything in a row but the wall time, the platform, the controller version and the ticket is a
function of the problem, the run's options and the solver module, so two honest machines give the
same values to the last byte. `SmtResultDigest.Canonical` writes exactly those values, one after
the other, each with its name and, for a text, its length (`smt-result/1`, then `index=12`,
`id[6]=cage-1`, and so on, every model and every value in order), and `SmtResultDigest.Of` is the
SHA-256 of its UTF-8 bytes in lower-case hexadecimal. A problem sent again in another package keeps
its digest. Copies of a problem solved on two machines are compared by it; a host recomputes it
from the row it received rather than trusting the one the row carries (`Holds`).

The bytes that are hashed are the row's own characters and never a stand-in for one. A text with
half of a surrogate pair alone is not valid UTF-16 and has no UTF-8; a row that holds one has no
digest: `Of` gives the empty text (`SmtResultDigest.NONE`), `Holds` is false whatever the row
carries, and neither throws. A node's session never writes such a text, so every row a node made
has its digest.

### The codes of a node's refusal

| Code | When |
|---|---|
| `version_mismatch` | the package was made for another controller version |
| `package_invalid` | the reference names no blob, no options, no size, or a number of problems outside 1 to 256 |
| `ticket_invalid` | the ticket is not 32 lower-case hex characters |
| `package_too_large` | the blob is larger than a node takes, by its reference or by what it unpacks to |
| `package_hash_mismatch` | the blob has another size or another SHA-256 than its reference names |
| `package_unreadable` | the blob is not a package of problems of this format, or a frame of it cannot be read |
| `package_mismatch` | the blob holds another number of problems, or other problems, than its reference names |
| `sandbox_unavailable` | the process has no sandbox for the solver |
| `memory_insufficient` | the machine's available memory does not hold one session under the package's options |
| `download_failed`, `upload_failed` | a transfer failed at every attempt |

## A run

What a lab sees of a run is the controller's documents (results, report, provenance,
integrity: JSON, with their schemas in the controller's `docs/`) and the preflight before it.
This package carries the types and the words they are made of, so that an application reads them
with the same vocabulary the controller writes them in.

| Type | Role |
|---|---|
| `SmtPreflightData` | what a problem set and a run's options come to before anything is sent (`Smt.Preflight`): the set counted (rows, taken, refused by code with the first 100 refusals in full, by logic, by query, by expected verdict, the stripped commands, the licences), the SHA-256 of the set, the options checked by name, the budget of a problem as numbers (the resource limit, the hard cap and its fuel, the memory, the models of an enumeration, the seed, the sampling rate), the memory a node needs for one session, the upper bound "taken problems times the hard cap" (a bound, never an estimate of a duration), and the packages a run would cut the set into |
| `SmtCountData` | one count of a table of the preflight: a name and how many times it was met |
| `SmtRunWords` | the words a run's documents add to a node's row: the host's statuses `no_result` and `disputed`; how an answer was settled (`one_copy`, `copies`, `dispute`, `agreement`); the evaluator's classes as a row writes them (`validated`, `partial`, `failed`, `none`); the agreement with the expected verdict (`agree`, `disagree`, `n/a`); the causes of a dispute (`digests_differ`, `model_failed`, `surplus`) and its outcomes (`resolved`, `no_majority`, `too_few_copies`, `surplus_copies`) |

A preflight is `Runnable` when its options are in range, its profile is the controller's and the
set has a row. Options out of range are said, one sentence an option, and the set is still read
with each such option at its default.
