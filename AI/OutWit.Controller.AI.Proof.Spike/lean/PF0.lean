/-
PF-0 spike: the measuring commands of the gate. This file is the harness's own code (trusted);
a candidate never contains these commands. ASCII only, on purpose.

  #pf0_measure "label" in <command>
      elaborates the command and reports the heartbeats it used on its thread (the counter
      Lean's own `maxHeartbeats` budget reads) and the time it took.

  #pf0_report <solution> <challenge>
      reports what the solution declared: its kind, whether its statement is the challenge's,
      the axioms it rests on, and the hashes of its statement and of its proof term.

Each report is one information message whose text is `PF0 ` followed by a JSON object.
-/
import Lean

open Lean Elab Command

namespace PF0

def emit (fields : List (Prod String Json)) : CommandElabM Unit :=
  logInfo (MessageData.ofFormat (Std.Format.text ("PF0 " ++ (Json.mkObj fields).compress)))

def kindOf (info : Option ConstantInfo) : String :=
  match info with
  | none => "missing"
  | some (ConstantInfo.thmInfo _) => "theorem"
  | some (ConstantInfo.defnInfo _) => "definition"
  | some (ConstantInfo.axiomInfo _) => "axiom"
  | some (ConstantInfo.opaqueInfo _) => "opaque"
  | some _ => "other"

elab "#pf0_measure " label:str " in " cmd:command : command => do
  let h0 <- IO.getNumHeartbeats
  let t0 <- IO.monoNanosNow
  try
    elabCommand cmd
  finally
    let h1 <- IO.getNumHeartbeats
    let t1 <- IO.monoNanosNow
    emit [("mark", Json.str label.getString), ("heartbeats", toJson (h1 - h0)),
      ("nanos", toJson (t1 - t0))]

elab "#pf0_report " sol:ident chal:ident : command => do
  let env <- getEnv
  let solName := sol.getId
  let s := env.find? solName
  let c := env.find? chal.getId
  let same :=
    match s, c with
    | some si, some ci => si.type == ci.type && si.levelParams == ci.levelParams
    | _, _ => false
  let axs : Array Name <-
    match s with
    | some _ => collectAxioms solName
    | none => pure #[]
  let typeHash :=
    match s with
    | some si => si.type.hash.toNat
    | none => 0
  let valueHash :=
    match s with
    | some (ConstantInfo.thmInfo v) => v.value.hash.toNat
    | some (ConstantInfo.defnInfo v) => v.value.hash.toNat
    | _ => 0
  emit [("report", Json.str solName.toString), ("kind", Json.str (kindOf s)),
    ("challenge_kind", Json.str (kindOf c)), ("same", Json.bool same),
    ("axioms", Json.arr (axs.map (fun n => Json.str n.toString))),
    ("type_hash", toJson typeHash), ("value_hash", toJson valueHash)]

end PF0
