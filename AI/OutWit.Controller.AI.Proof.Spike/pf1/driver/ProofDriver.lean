import Lean
import LeanExport.Parse
import Lake.Check.Compare
import Lake.Check.Axioms

/-!
# PF-1 spike: a prototype of the fast driver, and a slicer for one closure

`proofdriver fast <solution.ndjson> --challenge <challenge.ndjson> --theorems a,b [--axioms x,y]
  [--import Mathlib]`
  The fast stage of the export form: the trusted environment of the declared revision is imported
  from its compiled files; every constant of the export that the environment has must be identical
  to it (as the exporter writes it: no metadata, `letE` non-dependent flags cleared), else
  `library_mismatch`; the statement check and the axiom audit are `lake comparator`'s own functions;
  the remaining constants are replayed by Lean's kernel on top of the environment.

`proofdriver profile <export.ndjson> --out <times.tsv>`
  The whole closure replayed into an empty environment one declaration (one inductive block) at a
  time, each timed: the weights a split across machines would cut by.

`proofdriver slice <export.ndjson> --slices K --index I [--weights <times.tsv>]`
  One of K contiguous slices of the closure (in the export's order, which puts every dependency
  first) checked by the kernel, the slices before it added without checking. The union of the K
  verdicts is the verdict of a full replay; each slice still parses the export up to its end.

Every mode prints one JSON object on standard output and exits 0 when the verdict is `accepted`.
-/

open Lean

namespace ProofDriver

/-- The kernel's primitives `lake comparator` compares (Lake.Check.primitiveTargets). -/
def primitiveTargets : Array Name := #[
  ``Nat.add, ``Nat.sub, ``Nat.mul, ``Nat.pow, ``Nat.gcd, ``Nat.div, ``Nat.mod, ``Nat.beq, ``Nat.ble,
  ``Nat.land, ``Nat.lor, ``Nat.xor, ``Nat.shiftLeft, ``Nat.shiftRight, ``String.ofList, ``Char.ofNat,
  ``List, ``eagerReduce, ``Nat, ``String, ``String.mk, ``Char, ``optParam, ``autoParam,
  ``semiOutParam, ``outParam]

def standardAxioms : Array Name := #[``propext, ``Classical.choice, ``Quot.sound]

structure Args where
  mode : String := ""
  file : String := ""
  challenge : Option String := none
  theorems : Array Name := #[]
  axioms : Array Name := standardAxioms
  imports : Array Name := #[`Mathlib]
  slices : Nat := 1
  index : Nat := 0
  weights : Option String := none
  out : Option String := none

def splitNames (s : String) : Array Name :=
  (s.splitOn ",").toArray.filter (· ≠ "") |>.map String.toName

partial def parseArgs (args : List String) (acc : Args) : Except String Args :=
  match args with
  | [] => .ok acc
  | "--challenge" :: v :: rest => parseArgs rest { acc with challenge := some v }
  | "--theorems" :: v :: rest => parseArgs rest { acc with theorems := splitNames v }
  | "--axioms" :: v :: rest => parseArgs rest { acc with axioms := splitNames v }
  | "--import" :: v :: rest => parseArgs rest { acc with imports := splitNames v }
  | "--slices" :: v :: rest => parseArgs rest { acc with slices := v.toNat! }
  | "--index" :: v :: rest => parseArgs rest { acc with index := v.toNat! }
  | "--weights" :: v :: rest => parseArgs rest { acc with weights := some v }
  | "--out" :: v :: rest => parseArgs rest { acc with out := some v }
  | a :: rest =>
    if acc.mode.isEmpty then parseArgs rest { acc with mode := a }
    else if acc.file.isEmpty then parseArgs rest { acc with file := a }
    else .error s!"unexpected argument {a}"

/-- The first line of an export: the exporter, the Lean version, the format version. -/
def readMeta (file : String) : IO (Option Json) := do
  let line ← IO.FS.withFile file .read fun h => h.getLine
  match Json.parse line with
  | .ok j => return j.getObjVal? "meta" |>.toOption
  | .error _ => return none

def formatOk (meta? : Option Json) : Bool :=
  match meta? with
  | some m =>
    match m.getObjVal? "format" >>= (·.getObjValAs? String "version") with
    | .ok v => v.startsWith "3.1."
    | .error _ => false
  | none => false

def parseExport (file : String) : IO LeanExport.ExportedEnv :=
  IO.FS.withFile file .read fun h => LeanExport.parseStream (.ofHandle h)

/-- What the exporter does to an expression: metadata removed, `letE` marked dependent. -/
partial def norm (e : Expr) : Expr :=
  e.replace fun
    | .mdata _ b => some (norm b)
    | .letE n t v b true => some (.letE n (norm t) (norm v) (norm b) false)
    | _ => none

def normInfo : ConstantInfo → ConstantInfo
  | .defnInfo v => .defnInfo { v with type := norm v.type, value := norm v.value }
  | .thmInfo v => .thmInfo { v with type := norm v.type, value := norm v.value }
  | .opaqueInfo v => .opaqueInfo { v with type := norm v.type, value := norm v.value }
  | .axiomInfo v => .axiomInfo { v with type := norm v.type }
  | .quotInfo v => .quotInfo { v with type := norm v.type }
  | .inductInfo v => .inductInfo { v with type := norm v.type }
  | .ctorInfo v => .ctorInfo { v with type := norm v.type }
  | .recInfo v => .recInfo { v with type := norm v.type, rules := v.rules.map fun r => { r with rhs := norm r.rhs } }

def kindOf : ConstantInfo → String
  | .defnInfo _ => "def" | .thmInfo _ => "thm" | .opaqueInfo _ => "opaque" | .axiomInfo _ => "axiom"
  | .quotInfo _ => "quot" | .inductInfo _ => "inductive" | .ctorInfo _ => "ctor" | .recInfo _ => "rec"

/-- The first 2000 characters of a message (a kernel's message can hold a whole term). -/
def clip (s : String) : String :=
  (s.toList.take 2000).foldl (fun acc c => acc.push c) ""

def emit (fields : List (String × Json)) : IO Unit :=
  IO.println (Json.mkObj fields).compress

def ms (t0 t1 : Nat) : Json := toJson (t1 - t0)

def kernelMessage (ex : Kernel.Exception) : IO String := ex.toMessageData {} |>.toString

/-- The exported constants in the export's order, cut into blocks a kernel adds at once: a
constant, or an inductive block (its types, constructors and recursors, which the parser keeps
together). -/
def blocks (exported : LeanExport.ExportedEnv) : Array (Array Name) := Id.run do
  let mut out : Array (Array Name) := #[]
  let mut current : Array Name := #[]
  let mut inBlock : NameSet := {}
  for n in exported.constOrder do
    let some ci := exported.constMap[n]? | continue
    let joins :=
      match ci with
      | .ctorInfo _ | .recInfo _ => true
      | .inductInfo _ => inBlock.contains n
      | _ => false
    if joins && !current.isEmpty then
      current := current.push n
    else
      unless current.isEmpty do out := out.push current
      current := #[n]
      inBlock := {}
      if let .inductInfo v := ci then
        inBlock := v.all.foldl (·.insert ·) {}
  unless current.isEmpty do out := out.push current
  return out

/-- Adds one block without checking it (the inductive module still runs for an inductive). -/
def addUnchecked (env : Kernel.Environment) (exported : LeanExport.ExportedEnv) (block : Array Name) :
    Except String Kernel.Environment := do
  let some ci := exported.constMap[block[0]!]? | throw s!"missing {block[0]!}"
  let decl? : Option Declaration :=
    match ci with
    | .defnInfo v => some (.defnDecl v)
    | .thmInfo v => some (.thmDecl v)
    | .opaqueInfo v => some (.opaqueDecl v)
    | .axiomInfo v => some (.axiomDecl v)
    | .quotInfo _ => if env.find? ``Quot |>.isSome then none else some .quotDecl
    | .inductInfo v =>
      let types := v.all.filterMap fun n =>
        match (exported.constMap[n]? : Option ConstantInfo) with
        | some (.inductInfo iv) =>
          some { name := iv.name, type := iv.type,
                 ctors := iv.ctors.filterMap fun c =>
                   match (exported.constMap[c]? : Option ConstantInfo) with
                   | some (.ctorInfo cv) => some { name := cv.name, type := cv.type }
                   | _ => none }
        | _ => none
      some (.inductDecl v.levelParams v.numParams types v.isUnsafe)
    | _ => none
  match decl? with
  | none => return env
  | some decl =>
    match env.addDeclWithoutChecking decl with
    | .ok env' => return env'
    | .error _ => throw s!"could not add {block[0]!} without checking"

def blockMap (exported : LeanExport.ExportedEnv) (block : Array Name) : Std.HashMap Name ConstantInfo :=
  block.foldl (init := {}) fun m n =>
    match (exported.constMap[n]? : Option ConstantInfo) with
    | some ci => m.insert n ci
    | none => m

/-- The quotient package goes through the kernel first, as `leanchecker --from-export` does it:
`Eq`'s block, then `Quot` alone (the kernel adds `Quot.mk`, `Quot.lift` and `Quot.ind` with it and
sets the flag its type checker relies on). Returns what to replay and what the later steps skip. -/
def quotStart (exported : LeanExport.ExportedEnv) (bs : Array (Array Name)) :
    Std.HashMap Name ConstantInfo × NameSet := Id.run do
  let mut replayMap : Std.HashMap Name ConstantInfo := {}
  let mut skip : NameSet := {}
  if exported.constMap.contains ``Quot then
    for b in bs do
      if b.contains ``Eq then
        replayMap := b.foldl (init := replayMap) fun m n =>
          match (exported.constMap[n]? : Option ConstantInfo) with
          | some ci => m.insert n ci
          | none => m
        skip := b.foldl (·.insert ·) skip
    if let some ci := exported.constMap[``Quot]? then
      replayMap := replayMap.insert ``Quot ci
    for n in [``Quot, ``Quot.mk, ``Quot.lift, ``Quot.ind] do
      skip := skip.insert n
  return (replayMap, skip)

/-- The quotient constants the kernel made must be the ones the export carries. -/
def quotPostCheck (exported : LeanExport.ExportedEnv) (kenv : Kernel.Environment) : Except String Unit := do
  for n in [``Quot, ``Quot.mk, ``Quot.lift, ``Quot.ind] do
    if let some ci := exported.constMap[n]? then
      match kenv.find? n with
      | some ci' => if ci != ci' then throw s!"Quotient constant mismatch on: {n}"
      | none => throw s!"Could not find quotient constant in final kernel env: {n}"

unsafe def runFast (a : Args) : IO UInt32 := do
  let t0 ← IO.monoMsNow
  let meta? ← readMeta a.file
  unless formatOk meta? do
    emit [("mode", Json.str "fast"), ("verdict", Json.str "format_refused"), ("meta", meta?.getD .null)]
    return 3
  let exported ← parseExport a.file
  let t1 ← IO.monoMsNow
  let challenge? ← a.challenge.mapM parseExport
  let t2 ← IO.monoMsNow
  let env ← importModules (a.imports.map ({ module := · })) {}
  let kenv := env.toKernelEnv
  let t3 ← IO.monoMsNow
  -- Library constants must be the environment's own.
  let mut same := 0
  let mut mismatched : Array Name := #[]
  let mut fresh : Std.HashMap Name ConstantInfo := {}
  let mut freshKinds : Std.HashMap String Nat := {}
  for n in exported.constOrder do
    let some ci := exported.constMap[n]? | continue
    match kenv.find? n with
    | some ci' =>
      if normInfo ci' == ci then same := same + 1 else mismatched := mismatched.push n
    | none =>
      fresh := fresh.insert n ci
      freshKinds := freshKinds.insert (kindOf ci) (freshKinds.getD (kindOf ci) 0 + 1)
  let t4 ← IO.monoMsNow
  let common : List (String × Json) := [
    ("mode", Json.str "fast"), ("meta", meta?.getD .null), ("constants", toJson exported.constOrder.size),
    ("library_same", toJson same), ("library_mismatched", toJson mismatched.size),
    ("own", toJson fresh.size),
    ("own_kinds", Json.mkObj (freshKinds.toList.map fun (k, v) => (k, toJson v))),
    ("parse_ms", ms t0 t1), ("challenge_parse_ms", ms t1 t2), ("import_ms", ms t2 t3),
    ("compare_ms", ms t3 t4)]
  unless mismatched.isEmpty do
    emit (common ++ [("verdict", Json.str "library_mismatch"),
      ("first_mismatches", toJson ((mismatched.extract 0 10).map toString))])
    return 1
  -- The statement and the axioms, as `lake comparator` checks them.
  if let some challenge := challenge? then
    let targets := a.theorems ++ a.axioms
    match Lake.Check.compareAt challenge exported targets #[] primitiveTargets with
    | .error e =>
      emit (common ++ [("verdict", Json.str "statement_mismatch"), ("message", toJson e)])
      return 1
    | .ok () => pure ()
  match Lake.Check.checkAxioms exported a.theorems #[] a.axioms with
  | .error e =>
    emit (common ++ [("verdict", Json.str "axioms_refused"), ("message", toJson e)])
    return 1
  | .ok () => pure ()
  let t5 ← IO.monoMsNow
  -- The project's own declarations, through Lean's kernel, on top of the environment.
  try
    let _ ← kenv.replay fresh
    let t6 ← IO.monoMsNow
    emit (common ++ [("verdict", Json.str "accepted"), ("statement_ms", ms t4 t5), ("replay_ms", ms t5 t6),
      ("total_ms", ms t0 t6)])
    return 0
  catch e =>
    let t6 ← IO.monoMsNow
    emit (common ++ [("verdict", Json.str "kernel_rejected"), ("message", toJson (clip (toString e))),
      ("statement_ms", ms t4 t5), ("replay_ms", ms t5 t6), ("total_ms", ms t0 t6)])
    return 1

def readWeights (path : String) : IO (Std.HashMap Name Nat) := do
  let text ← IO.FS.readFile path
  let mut m : Std.HashMap Name Nat := {}
  for line in text.splitOn "\n" do
    match line.splitOn "\t" with
    | [n, _, t] => m := m.insert n.toName (t.toNat?.getD 0)
    | _ => pure ()
  return m

/-- Cuts the blocks into `k` contiguous slices of about equal weight. -/
def cut (bs : Array (Array Name)) (weight : Array Name → Nat) (k : Nat) : Array (Nat × Nat) := Id.run do
  let total := bs.foldl (fun acc b => acc + weight b) 0
  let mut out : Array (Nat × Nat) := #[]
  let mut start := 0
  let mut acc := 0
  for i in [0:bs.size] do
    acc := acc + weight bs[i]!
    if out.size + 1 < k && acc * k ≥ total * (out.size + 1) then
      out := out.push (start, i + 1)
      start := i + 1
  out := out.push (start, bs.size)
  while out.size < k do out := out.push (bs.size, bs.size)
  return out

unsafe def runSlice (a : Args) : IO UInt32 := do
  let t0 ← IO.monoMsNow
  let meta? ← readMeta a.file
  unless formatOk meta? do
    emit [("mode", Json.str "slice"), ("verdict", Json.str "format_refused"), ("meta", meta?.getD .null)]
    return 3
  let exported ← parseExport a.file
  let t1 ← IO.monoMsNow
  let bs := blocks exported
  let weights ← match a.weights with
    | some p => readWeights p
    | none => pure {}
  let weight : Array Name → Nat := fun b =>
    if weights.isEmpty then b.size else b.foldl (fun acc n => acc + weights.getD n 0) 1
  let ranges := cut bs weight a.slices
  let some (lo, hi) := ranges[a.index]? | throw <| IO.userError "index out of range"
  let mut kenv := (← mkEmptyEnvironment).toKernelEnv
  -- The quotient package first, checked (it is small and sets the kernel's flag).
  let (quot, skip) := quotStart exported bs
  let mut unchecked := 0
  try
    unless quot.isEmpty do kenv ← kenv.replay quot
    for b in bs.extract 0 lo do
      if b.any (skip.contains ·) then continue
      match addUnchecked kenv exported b with
      | .ok e => kenv := e; unchecked := unchecked + b.size
      | .error e => throw <| IO.userError e
  catch e =>
    emit [("mode", Json.str "slice"), ("verdict", Json.str "load_error"), ("message", toJson (clip (toString e)))]
    return 2
  let t2 ← IO.monoMsNow
  let mut todo : Std.HashMap Name ConstantInfo := {}
  for b in bs.extract lo hi do
    if b.any (skip.contains ·) then continue
    todo := b.foldl (init := todo) fun m n =>
      match (exported.constMap[n]? : Option ConstantInfo) with
      | some ci => m.insert n ci
      | none => m
  let common : List (String × Json) := [
    ("mode", Json.str "slice"), ("slices", toJson a.slices), ("index", toJson a.index),
    ("blocks", toJson bs.size), ("slice_blocks", toJson (hi - lo)), ("slice_constants", toJson todo.size),
    ("unchecked_constants", toJson unchecked), ("constants", toJson exported.constOrder.size),
    ("parse_ms", ms t0 t1), ("load_ms", ms t1 t2)]
  try
    let done ← kenv.replay todo
    if a.index + 1 == a.slices then
      if let .error e := quotPostCheck exported done then throw <| IO.userError e
    let t3 ← IO.monoMsNow
    emit (common ++ [("verdict", Json.str "accepted"), ("check_ms", ms t2 t3), ("total_ms", ms t0 t3)])
    return 0
  catch e =>
    let t3 ← IO.monoMsNow
    emit (common ++ [("verdict", Json.str "kernel_rejected"), ("message", toJson (clip (toString e))),
      ("check_ms", ms t2 t3), ("total_ms", ms t0 t3)])
    return 1

unsafe def runProfile (a : Args) : IO UInt32 := do
  let t0 ← IO.monoMsNow
  let meta? ← readMeta a.file
  unless formatOk meta? do
    emit [("mode", Json.str "profile"), ("verdict", Json.str "format_refused"), ("meta", meta?.getD .null)]
    return 3
  let exported ← parseExport a.file
  let t1 ← IO.monoMsNow
  let bs := blocks exported
  let mut kenv := (← mkEmptyEnvironment).toKernelEnv
  let (quot, skip) := quotStart exported bs
  let out := a.out.getD "times.tsv"
  let h ← IO.FS.Handle.mk out .write
  let mut slowest : Array (Nat × Name) := #[]
  let mut verdict := "accepted"
  let mut message := ""
  try
    unless quot.isEmpty do kenv ← kenv.replay quot
    for b in bs do
      if b.any (skip.contains ·) then continue
      let s ← IO.monoNanosNow
      kenv ← kenv.replay (blockMap exported b)
      let e ← IO.monoNanosNow
      let us := (e - s) / 1000
      let some ci := exported.constMap[b[0]!]? | continue
      h.putStrLn s!"{b[0]!}\t{kindOf ci}\t{us}"
      if slowest.size < 20 || us > (slowest.back?.map (·.1)).getD 0 then
        slowest := (slowest.push (us, b[0]!)).qsort (fun x y => x.1 > y.1) |>.extract 0 20
    if let .error e := quotPostCheck exported kenv then throw <| IO.userError e
  catch e =>
    verdict := "kernel_rejected"
    message := clip (toString e)
  h.flush
  let t2 ← IO.monoMsNow
  emit [("mode", Json.str "profile"), ("verdict", toJson verdict), ("message", toJson message),
    ("blocks", toJson bs.size), ("constants", toJson exported.constOrder.size),
    ("parse_ms", ms t0 t1), ("replay_ms", ms t1 t2),
    ("slowest_us", toJson (slowest.map fun (t, n) => Json.mkObj [("name", toJson n.toString), ("us", toJson t)]))]
  return if verdict == "accepted" then 0 else 1

unsafe def run (args : List String) : IO UInt32 := do
  let a ← match parseArgs args {} with
    | .ok a => pure a
    | .error e => IO.eprintln e; return 2
  initSearchPath (← findSysroot)
  match a.mode with
  | "fast" => runFast a
  | "slice" => runSlice a
  | "profile" => runProfile a
  | m => IO.eprintln s!"unknown mode {m}"; return 2

end ProofDriver

unsafe def main (args : List String) : IO UInt32 :=
  ProofDriver.run args
