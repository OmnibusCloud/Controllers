# What Verify runs on your computer

For someone who lends a computer to the network and wants to know what the Verify
controller does with it. Every number on this page is the code's own, and a test fails when
the two part. What the sandbox does and refuses in the first three sections is run by the
tests listed under "How to check this yourself". What is an estimate, what rests on something
outside this controller, and what no test here can show is in the last two sections.

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
| Processor | a fixed budget of instructions for every run: 20 thousand million by default, never more than 240 thousand million. The default is about 10 seconds of a plain busy loop on a slow machine, the largest two minutes of one. When the budget runs out the program is stopped. What the sandbox does at a program's request (reading a file, printing, making random bytes, a sleep) is paid from the same budget: a thousand units a request, and one unit a byte it moves. A task that comes with test cases is run once for each case, 1,024 cases at most, and every run has the budget |
| A run that drags | the budget counts work, not time, and some work is slow. So your computer also keeps a clock of its own on every run and ends it after 50 seconds for the default budget, a little over 8 minutes for the largest. That stop is your computer's own and says nothing about the program: the task is sent out once more, and if no computer does better it is reported as having no result |
| Memory | 256 MB by default (never more than 2 GB), one block of it. Asking for more fails inside the program |
| Files | only the language's own library, or the task's own files, read-only, and only under the exact names they have. It cannot write, change, delete or rename a file, and cannot list a folder. What it can read about a file says nothing of your computer: the file's times and its disk read as zero, and its file number follows from its name inside the sandbox |
| Your files | none. Your disk does not exist for it |
| Network | none |
| Other programs | it cannot start any |
| Time | not yours: a clock that starts at zero and moves only when the program sleeps. A sleep returns at once |
| Randomness | not yours: a sequence fixed by the task |
| Your environment | none: no user name, no paths, no variables |
| Output | what a task prints is kept up to a cap: 256 KB by default, never more than 8 MB, for all its test cases together. A byte past the cap is never stored, in memory or on disk, and the write that passes the cap ends the run. Error output past its own cap (64 KB by default, never more than 1 MB) is dropped and the run goes on |
| A crash | ends the program, and only the program: the sandbox catches every crash (a bad pointer, a stack overflow, an `abort`). A bad pointer handed to the sandbox itself is answered with an error code inside the program. On a Windows computer whose processor and system enforce stack protection, the client runs with it switched on, and a crash is still caught as an ordinary check, so the client keeps running and keeps the protection |

## What it takes from the machine

- **Processor.** At most one program a core at a time, 16 at most unless the lab sets
  another number (never more than your cores), and fewer when memory is short: the limit
  is three quarters of the memory that is free when a batch starts, divided by the memory
  one program may take (for C and C++ tasks the compiler's 1 GB counts when it is the
  larger). For tasks whose program is turned into machine code on your computer (below),
  what that work may take is counted too: 64 MB a program, and about 1.8 GB set aside
  once. The work runs below normal priority: on Windows your own programs go first.
  Linux does not honour that setting for threads; there the cap on the number of programs
  is the protection.
- **Compiling C and C++.** A C or C++ task is compiled on your computer. The compiler is
  itself a program in the same sandbox, with a budget of its own: 100 thousand million
  instructions for all the steps of a build and 1 GB of memory a step. It can read only
  the task's sources and the language's own library. What it produces (the object files
  and the program) is written to files in the client's own folder, each cut at 64 MB at
  the write and all of one build together at 128 MB, and deleted after the task ends.
- **Turning a module into machine code.** A program built from C or C++, and a module a
  lab compiled itself, is turned into machine code on your computer before it runs. That
  work takes the one thread of the worker that runs the task, so it stays within the
  cores counted above. Before it starts, the module is checked: its shape is held to
  bounds (how many values a call or a block passes, how many locals a function has and
  how large it is, how many functions there are, how deep blocks nest) and priced, and
  the price is taken from the task's budget of instructions: a task that cannot pay is
  stopped and nothing is compiled. The prices were fitted to what the engine was measured
  to take for each shape, so that a budget buys about as much of this work as it buys of
  running, to within a few times either way: some ten seconds on a slow machine at the
  default budget, about two minutes at the largest. That holds for the shapes that were
  measured; it is not a proof about every module, and a compile cannot be stopped once it
  has started.
  Some code compiles far more slowly than its price says: calls through a table, and on a
  computer whose processor enforces hardware stack protection (where every trap is an
  explicit check) loads and stores. Within every bound and the default budget, a module of
  many such functions can hold one core of your computer for minutes while it compiles. A
  compile cannot be stopped once it has started. No verdict is wrong because of it, and the
  memory it takes is bounded as below. While it compiles, the engine also takes memory that is in no program's
  256 MB: little for an ordinary program. It is estimated from the module before anything is
  compiled (the code of every function, its calls, loads and stores, branches and the
  instructions that may trap), and a module whose compile may take more than 1.5 GB is
  refused. Compiles that may take 64 MB or more share 1.5 GB between them, so the largest
  runs alone, and the number of programs run at once is lowered for those 1.5 GB (above).
  The estimate holds for the kinds of code that were measured; like the price, it is not a
  proof about every module. The machine code of the programs of a batch is
  kept in memory while the batch may need it again, 256 MB of it at most. A module past
  a bound (blocks nested deeper than 2,048 among them),
  or one a lab brings that is larger than 8 MB, is refused. The first use of a language
  in a process compiles that language's own module (the controller's, checked by its
  SHA-256) on the engine's threads, once.
- **Disk.** A program's output never reaches the disk. What does: the files a program's
  input and a task's sources are given through, the compiler's products, and a package
  of results until it is uploaded. All of it is kept in the client's own folders and
  deleted soon after its task or its package is done (a task's files in the background,
  so that a file scanner that holds a delete does not hold up the work). The task packages your computer
  downloads are kept in the client's own cache. Nothing is written anywhere else.
- **Network.** The client downloads the tasks and uploads the results. The programs
  themselves send and receive nothing. A transfer has a time to finish in (two minutes
  plus the time its bytes take at 32 KB a second), so one that hangs does not hold up
  the others.
- **What your computer refuses.** It checks a package before it runs any of it. A package
  made by another version of the controller, one larger than a package may be (4,096
  tasks at most), one with a task of more files, arguments or test cases than a task may
  have (1,024 of each), or one for a language whose files are missing or are not the pinned
  ones is refused whole, and the network gives it to another computer.

## What leaves your computer

For each program: what it printed, its exit code, the verdict, and how much it cost
(instructions, memory, time). For the run: the kind of system (for example "windows-x64")
and the versions of the controller and of the engine. How many programs your computer ran
at once and how long a batch took stay on your computer: with the limits the lab's to
choose, your cores and your memory could be read off those numbers.

Your computer's name, your user name and your address are not in any of it. When
something goes wrong on your computer (a language file that is missing, a transfer that
failed, a disk that is full), what travels is fixed words or a short code, without a
path, an account name or the text of an error. The detail stays in the client's own log.
If the log itself fails (it cannot take a line, or two lines at once), the line is lost and
nothing else: the work goes on, and the log's error travels nowhere.
What a program itself printed is the program's and travels as it is.

## How to check this yourself

The claims above are tests in this repository, run on Windows, Linux and macOS at every
change:

- `VerifyAdversarialTests`: programs that try to open a network connection, read a file
  of yours, write to the library, climb out of their folder, start a process, loop forever,
  eat all memory, flood the output, sleep for an hour, list a folder, change a file's time.
- `VerifyNodeProtectionTests`: what a hostile task cannot do to the computer that runs it:
  a flood of output that never reaches the disk, a module that is priced before it is
  compiled, a wild pointer that is the task's own error, a run the clock ends that has no
  verdict, a batch held to the ceilings on the node.
- `VerifyWasiRuntimeTests`: hand-made modules that try to grow a table without end, ask
  for a second block of memory or a 64-bit one, read outside their memory, or call what
  the sandbox does not provide.
- `VerifySandboxHygieneTests`: nothing is left on the disk after a run.
- `VerifyHostProtectionTests`: a program that crashes in each way a program can (an
  unreachable instruction, a bad memory access, a division by zero, a stack overflow) inside
  a host built like the client ends as a crash and leaves the host running. On a machine that
  enforces the processor's stack protection that is the proof that the client survives under
  it; on a machine that does not, that one test is reported as skipped and says the proof
  did not run there. Either way a Windows run states which it was in one line of its log:
  `stack-protection proof: shadow stacks on` or `stack-protection proof: shadow stacks off`.
  A release is cut from a commit whose Windows run states the first. The way crashes are
  caught under the protection is run on every machine all the same, by asking for it.
- `VerifyTrapModeTests`: the same programs give the same verdict, output and cost whichever
  of the two ways a crash is caught in.
- `VerifyHostileKitTests`: the fifteen hostile tasks that are run on the test fleet, each
  with the verdict it must get.
- `VerifyLoggerFateTests`: on a client whose log throws at every line, and on one whose log
  cannot take two lines at once, every package of a share completes with its results, and a
  package that fails for a reason of the computer's own still says only the fixed words.
- `VerifyClangRuntimeTests`: a C or C++ build leaves nothing behind, and the program it
  builds sees no file system.
- `WasmHostNativeResolutionTests`: in a host loaded like the client, an engine library that is
  not the pinned one keeps the sandbox off, and another controller without its engine library,
  or with another one, loaded first, does not stop the sandbox.

`dotnet test AI/OutWit.Controller.AI.Verify.Tests` runs them on your machine.

## What an answer rests on

Your computer's answers are checked, and so are everybody else's. This is what the
network does with them, and how far it goes.

- **An answer counts only for the work that was given.** Every package of tasks goes out
  with a ticket, 16 random bytes, and comes back with it. The server accepts an answer only
  under a ticket it gave out for that package, made by the same version of the controller
  and of the engine, for exactly the package's tasks. Anything else is dropped and counted.
- **A share of the work is done twice,** on two different computers that cannot tell they
  are being checked. The server never chooses between two answers. Answers that agree are
  accepted. Answers that disagree send the task out three more times, and the result that
  at least two of those three computers return, with no tie, is accepted. Otherwise the
  task is reported as disputed and counts as no result.
- **A computer's own trouble is never a verdict.** A task a computer could not run (its
  clock ended it, a disk was full) is sent out once more, and if nobody does better it is
  reported as having no result.

Where that ends:

- **Work that is not done twice rests on one computer.** How large the share is that is
  done twice is the lab's to set, and it may be none. A task that was not checked, and a
  task that was sent out once more after a computer's fault, has the answer of the one
  computer that ran it. The documents of a run say how many tasks those are.
- **Counting computers rests on the platform.** The server sees answers, not computers. It
  reads three answers as three computers because the platform keeps the copies of a package
  on different computers and, from Grid 1.3.0 on, takes at most one answer from a computer
  for each package it was given. With an older Grid a computer that answers twice can take
  the place of one that did not answer. What the controller does alone, whatever it is
  sent: an answer under a ticket that was not given is dropped, and more answers than were
  asked for are never taken as agreement.
- **Two computers that tell the same lie about a task are the majority** wherever they hold
  two of its three copies. One computer that lies can keep a disputed task from being
  settled; it cannot make its answer the accepted one while the other copies come back.
- **A run's clock is your computer's own stop, not the program's.** It is sized from the
  budget (50 seconds for the default budget, a little over 8 minutes for the largest), it
  gives no verdict, and a task it ended is sent out once more. On the test fleet no task
  of the hostile kit has reached it: a sleep takes no time, and what a program asks of the
  sandbox is paid from its budget by the byte. A run of slow instructions on a slow
  computer could reach it. That it works is shown by a test that shortens it.

## Where the protection ends

- **The sandbox is the wasmtime engine.** A flaw in the engine itself is a flaw here.
  This version stands on the engine's long-term line 48, which its makers support with
  security fixes until August 2028: the native library of wasmtime 48.0.8, its newest
  patch, under the .NET binding wasmtime 48.0.2. The controller checks at start that the
  engine library it runs on is exactly that file (by its SHA-256): with any other, an older
  patch, a damaged copy, one another controller brought, the sandbox stays off. When a new
  patch of the line comes out, the controller takes it; within the line a patch changes
  neither what a program prints nor what it costs. The sandbox also leaves out the engine's features no task needs
  (exceptions, shared memory, the garbage-collected heap, 64-bit memories) and takes away
  the few system calls that were the subject of past advisories, each named in the code
  next to what closes it.
- **A program can use what it is given.** For as long as it runs it can keep a core busy
  and fill its memory. That is the work, not an escape.
- **The limits are the lab's to set, up to a ceiling.** A lab can ask for up to
  240 thousand million instructions and 2 GB for a run; it cannot ask for more. Your
  computer holds every batch to the ceilings itself, whatever the batch says. The limits
  are a run's, and a task with test cases is run once for each: one task can keep a core
  busy for as many runs as it has cases, 1,024 at most.
- **The budget bounds work, not time.** How long an instruction takes depends on what the
  program does: a loop of additions burns several thousand million a second, a chain of
  square roots or of cache misses a few hundred million. The clock your computer keeps on
  every run is what bounds the time.
- **The price of a module is an estimate, and this part is only partly closed.** The price
  is fitted to measurements, not proved for every module: a shape nobody measured could be
  priced too low. Turning a module into machine code cannot be interrupted once it has
  started, and the clock your computer keeps on a run does not cover it; the expensive ones
  run one at a time in the client's process. The memory that work takes is the client's
  own, not the program's 2 GB.
- **This page is about the Verify controller.** Other controllers the client runs have
  their own rules.
