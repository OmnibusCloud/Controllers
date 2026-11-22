# OutWit.Controller.AI.Smt.Model

The language layer and the data types of the OutWit Smt controller: SMT problems solved by a
pinned solver on anonymous nodes, with every answer checked.

This page describes **0.1.0**, the first part of the package: the reader. The controller is in
development; the sanitizer, the canonical form of a problem, the model evaluator and the wire
types join this package before a controller that needs them is published.

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
- a string literal or a quoted symbol holds no control character but the tab and the line
  breaks, and no half of a surrogate pair.

`-5` is a simple symbol, as the standard has it; a consumer that hands text to a solver which
reads `-5` as a number refuses such a symbol by a rule of its own.

A comment is dropped. A symbol between bars is the simple symbol of the same name (`|abc|` is
`abc`) and is written without bars, except a reserved word, which stays between them. Nesting is
kept on a stack of the reader's own: a term two hundred thousand deep is read, written and
compared without a deep call stack, and how deep a submitted problem may nest is a limit, not
a property of the thread.

| Ceiling of a submitted problem | |
|---|---|
| Characters | 4,194,304 |
| Tokens (atoms and parentheses) | 2,000,000 |
| Nesting depth | 2,000 |
| Digits of a numeral or a decimal | 4,096 |
| Bits of a hexadecimal or binary literal | 65,536 |
| Characters of a string literal | 65,536 |
