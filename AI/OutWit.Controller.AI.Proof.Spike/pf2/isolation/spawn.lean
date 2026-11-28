/-!
PF-2 spike: how a Lean program starts a child process inside a sandbox, as `lake comparator`'s
`whichExe` does (`IO.Process.output { cmd := "which", args := #[name] }`), with the error Lean reports
when the start fails.

    lean --run spawn.lean <command> [<command> ...]

Each command is started with the argument `git`; one line per command: its exit code and output, or
the error.
-/

def tryRun (cmd : String) (args : Array String) : IO Unit := do
  try
    let out ← IO.Process.output { cmd := cmd, args := args }
    IO.println s!"spawn {cmd} {args}: exit {out.exitCode}; stdout '{out.stdout.trimAscii.toString}'; stderr '{out.stderr.trimAscii.toString}'"
  catch e =>
    IO.println s!"spawn {cmd} {args}: ERROR {e}"

def main (args : List String) : IO UInt32 := do
  for cmd in args do
    tryRun cmd #["git"]
  tryRun "cmd.exe" #["/c", "exit", "0"]
  return 0
