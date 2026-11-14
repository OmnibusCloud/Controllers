# What Verify runs on your computer

For someone who lends a computer to the network and wants to know what the Verify
controller does with it. Everything on this page is something the code does and a test
checks; the last section says where the protection ends.

## What it is asked to do

A research lab sends a set of small programs and asks: does each one do what it should?
Your computer runs some of those programs and reports what each printed and whether it
finished. The programs are written by people you do not know, often by a language model.
They are treated as hostile.

## Where a program runs

Not on your computer as you know it. Each program runs inside a WebAssembly sandbox
(the wasmtime engine): a box with its own memory and no door to the operating system
except the few the sandbox opens. A new box is made for every run and thrown away after it.

What a program has inside the box:

| | |
|---|---|
| Processor | a fixed budget of instructions, and a time limit of 10 seconds by default (never more than 2 minutes). When either runs out the program is stopped |
| Memory | 256 MB by default (never more than 2 GB), one block of it. Asking for more fails inside the program |
| Files | only the language's own library, or the task's own files, read-only. It cannot write, change, delete or rename a file, and cannot list a folder |
| Your files | none. Your disk does not exist for it |
| Network | none |
| Other programs | it cannot start any |
| Time | not yours: a clock that starts at zero and moves only when the program sleeps. A sleep returns at once |
| Randomness | not yours: a sequence fixed by the task |
| Your environment | none: no user name, no paths, no variables |
| Output | what it prints, cut at 256 KB by default |
| A crash | ends the program, and only the program: the sandbox catches every crash (a bad pointer, a stack overflow, an `abort`). On Windows the client runs with the processor's stack protection switched on, and a crash is still caught as an ordinary check, so the client keeps running and keeps the protection |

## What it takes from the machine

- **Processor.** At most one program a core at a time, 16 at most unless the lab asks
  for more (never more than your cores), and fewer when memory is short (the limit is
  three quarters of your RAM divided by the memory of one program). The work runs below
  normal priority: on Windows your own programs go first. Linux does not honour that
  setting for threads; there the cap on the number of programs is the protection.
- **Compiling.** A C or C++ task is compiled on your computer. The compiler is itself a
  program in the same sandbox, with a budget of its own (a fixed number of instructions,
  1 GB of memory a step), so it can read only the task's sources and the language's own
  library, and writes nothing to your disk.
- **Disk.** The task files and the files a program's input and output pass through are
  kept in the client's own folders and deleted when the run ends. Nothing is written
  anywhere else.
- **Network.** The client downloads the tasks and uploads the results. The programs
  themselves send and receive nothing.

## What leaves your computer

For each program: what it printed, its exit code, the verdict, and how much it cost
(instructions, memory, time). For the run: the kind of system (for example "windows-x64"),
the versions of the controller and of the engine, and how many programs ran at once.
Your computer's name, your user name and your address are not in any of it.

## How to check this yourself

The claims above are tests in this repository, run on Windows, Linux and macOS at every
change:

- `VerifyAdversarialTests`: programs that try to open a network connection, read a file
  of yours, write to the library, climb out of their folder, start a process, loop forever,
  eat all memory, flood the output, sleep for an hour, list a folder, change a file's time.
- `VerifyWasiRuntimeTests`: hand-made modules that try to grow a table without end, ask
  for a second block of memory or a 64-bit one, read outside their memory, or call what
  the sandbox does not provide.
- `VerifySandboxHygieneTests`: nothing is left on the disk after a run.
- `VerifyHostProtectionTests`: a program that crashes in each way a program can (an
  unreachable instruction, a bad memory access, a division by zero, a stack overflow) inside
  a host built like the client, with the processor's stack protection on, ends as a crash
  and leaves the host running.
- `VerifyClangRuntimeTests`: a C or C++ build leaves nothing behind, and the program it
  builds sees no file system.

`dotnet test AI/OutWit.Controller.AI.Verify.Tests` runs them on your machine.

## Where the protection ends

- **The sandbox is the wasmtime engine.** A flaw in the engine itself is a flaw here.
  This version stands on the engine's long-term line 48, which its makers support with
  security fixes until August 2028: the native library of wasmtime 48.0.8, its newest
  patch, under the .NET binding wasmtime 48.0.2. When a new patch of the line comes out,
  the controller takes it; within the line a patch changes neither what a program prints
  nor what it costs. The sandbox also leaves out the engine's features no task needs
  (exceptions, shared memory, the garbage-collected heap, 64-bit memories) and takes away
  the few system calls that were the subject of past advisories, each named in the code
  next to what closes it.
- **A program can use what it is given.** For the seconds it runs it can keep a core busy
  and fill its memory. That is the work, not an escape.
- **The limits are the lab's to set, up to a ceiling.** A lab can ask for up to 2 minutes
  and 2 GB a program; it cannot ask for more.
- **This page is about the Verify controller.** Other controllers the client runs have
  their own rules.
