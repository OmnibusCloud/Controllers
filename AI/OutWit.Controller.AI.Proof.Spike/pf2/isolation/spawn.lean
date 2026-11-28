/-!
PF-2 spike: how a Lean program starts a child process inside a sandbox, as `lake comparator`'s
`whichExe` does (`IO.Process.output { cmd := "which", args := #[name] }`), with the error Lean reports
when the start fails. Each command is started three ways, to tell the pipes from the start itself:
`output` (standard input inherited, output and error piped: two `CreatePipe` calls on Windows, as
Lake), `inherit` (input from the null device, output and error inherited: no pipe), `one-pipe`
(input null, output piped, error null: one pipe).

    lean --run spawn.lean <command> [<command> ...]

Each command is started with the argument `git`; one line per command and way: its exit code and
output, or the error.
-/

def tryOutput (cmd : String) (args : Array String) : IO Unit := do
  try
    let out ← IO.Process.output { cmd := cmd, args := args }
    IO.println s!"spawn output {cmd} {args}: exit {out.exitCode}; stdout '{out.stdout.trimAscii.toString}'; stderr '{out.stderr.trimAscii.toString}'"
  catch e =>
    IO.println s!"spawn output {cmd} {args}: ERROR {e}"

def tryInherit (cmd : String) (args : Array String) : IO Unit := do
  try
    let child ← IO.Process.spawn { cmd := cmd, args := args, stdin := .null, stdout := .inherit, stderr := .inherit }
    let code ← child.wait
    IO.println s!"spawn inherit {cmd} {args}: exit {code}"
  catch e =>
    IO.println s!"spawn inherit {cmd} {args}: ERROR {e}"

def tryOnePipe (cmd : String) (args : Array String) : IO Unit := do
  try
    let child ← IO.Process.spawn { cmd := cmd, args := args, stdin := .null, stdout := .piped, stderr := .null }
    let text ← child.stdout.readToEnd
    let code ← child.wait
    IO.println s!"spawn one-pipe {cmd} {args}: exit {code}; stdout '{text.trimAscii.toString}'"
  catch e =>
    IO.println s!"spawn one-pipe {cmd} {args}: ERROR {e}"

def tryAll (cmd : String) (args : Array String) : IO Unit := do
  tryOutput cmd args
  tryInherit cmd args
  tryOnePipe cmd args

def main (args : List String) : IO UInt32 := do
  for cmd in args do
    tryAll cmd #["git"]
  tryAll "cmd.exe" #["/c", "exit", "0"]
  return 0
